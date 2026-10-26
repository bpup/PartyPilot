package navigation

import (
	"context"
	"math"
	"sync"
	"time"
)

type PathState string

const (
	PathPending     PathState = "pending"
	PathReady       PathState = "ready"
	PathUnreachable PathState = "unreachable"
)

type PathPlan struct {
	State PathState
	Route []Waypoint
}

type pathKey struct {
	terrain *Grid
	startX  int
	startY  int
	goalX   float64
	goalY   float64
	avoidX  int
	avoidY  int
}

type pathEntry struct {
	key         pathKey
	from        Waypoint
	avoid       Waypoint
	priority    int
	requestedAt time.Time
	completedAt time.Time
	plan        PathPlan
	running     bool
	ctx         context.Context
	cancel      context.CancelFunc
}

type pathSearch func(context.Context, *Grid, Waypoint, Waypoint, Waypoint) []Waypoint

// PathPlanner follows the target build's bounded one-worker queue and cache
// lifetimes. A Grid submitted here must remain immutable until the request
// finishes. Request returns pending without blocking its caller on A* search.
type PathPlanner struct {
	mu            sync.Mutex
	cache         map[pathKey]*pathEntry
	queued        []*pathEntry
	workerRunning bool
	closed        bool
	lastSweep     time.Time
	searchCount   int
	clock         func() time.Time
	search        pathSearch
}

func NewPathPlanner() *PathPlanner {
	return newPathPlanner(func(ctx context.Context, grid *Grid,
		from, goal, avoid Waypoint) []Waypoint {
		points, ok := grid.TargetWaypointsAvoidContext(ctx, from.X, from.Y,
			goal.X, goal.Y, avoid.X, avoid.Y, 5)
		if !ok {
			return nil
		}
		return points
	}, time.Now)
}

func newPathPlanner(search pathSearch, clock func() time.Time) *PathPlanner {
	return &PathPlanner{cache: make(map[pathKey]*pathEntry), search: search, clock: clock}
}

func (p *PathPlanner) Request(grid *Grid, from, goal, avoid Waypoint, priority int) PathPlan {
	if grid == nil || !grid.Valid() || !grid.TargetNavigation ||
		!finite(from.X) || !finite(from.Y) || !finite(goal.X) || !finite(goal.Y) ||
		!finite(avoid.X) || !finite(avoid.Y) {
		return PathPlan{State: PathUnreachable}
	}
	for _, value := range []Waypoint{from, goal, avoid} {
		x := (value.X - grid.OriginX) / grid.CellSize
		y := (value.Y - grid.OriginY) / grid.CellSize
		if !finite(x) || !finite(y) || x < -12 || y < -12 ||
			x > float64(grid.Width+12) || y > float64(grid.Height+12) {
			return PathPlan{State: PathUnreachable}
		}
	}
	key := pathKey{terrain: grid,
		startX: int(math.Floor((from.X - grid.OriginX) / grid.CellSize)),
		startY: int(math.Floor((from.Y - grid.OriginY) / grid.CellSize)),
		goalX:  goal.X, goalY: goal.Y,
		avoidX: int(math.Floor((avoid.X - grid.OriginX) / grid.CellSize)),
		avoidY: int(math.Floor((avoid.Y - grid.OriginY) / grid.CellSize))}
	now := p.clock()
	p.mu.Lock()
	if p.closed {
		p.mu.Unlock()
		return PathPlan{State: PathUnreachable}
	}
	if now.Sub(p.lastSweep) >= 500*time.Millisecond {
		p.lastSweep = now
		for _, entry := range p.cache {
			if !entry.running && ((entry.plan.State == PathPending && now.Sub(entry.requestedAt) > time.Second) ||
				(entry.plan.State != PathPending && now.Sub(entry.completedAt) > planLifetime(entry.plan.State))) {
				p.removeLocked(entry)
			}
		}
	}
	if entry := p.cache[key]; entry != nil {
		entry.requestedAt = now
		if priority < entry.priority {
			entry.priority = priority
		}
		if entry.plan.State != PathPending && now.Sub(entry.completedAt) > planLifetime(entry.plan.State) {
			p.removeLocked(entry)
		} else {
			plan := PathPlan{State: entry.plan.State, Route: append([]Waypoint(nil), entry.plan.Route...)}
			p.mu.Unlock()
			if plan.State == PathReady && (len(plan.Route) == 0 ||
				!grid.HasClearFollowSegmentAvoid(from.X, from.Y,
					plan.Route[0].X, plan.Route[0].Y, avoid.X, avoid.Y, 5)) {
				p.mu.Lock()
				if p.cache[key] == entry && !entry.running {
					p.removeLocked(entry)
				}
				p.mu.Unlock()
				return PathPlan{State: PathPending}
			}
			return plan
		}
	}
	if len(p.queued) >= 64 {
		p.mu.Unlock()
		return PathPlan{State: PathPending}
	}
	if len(p.cache) >= 256 {
		var oldest *pathEntry
		for _, candidate := range p.cache {
			if !candidate.running && (oldest == nil || candidate.requestedAt.Before(oldest.requestedAt)) {
				oldest = candidate
			}
		}
		if oldest == nil {
			p.mu.Unlock()
			return PathPlan{State: PathPending}
		}
		p.removeLocked(oldest)
	}
	ctx, cancel := context.WithCancel(context.Background())
	entry := &pathEntry{key: key, from: from, avoid: avoid, priority: priority,
		requestedAt: now, plan: PathPlan{State: PathPending}, ctx: ctx, cancel: cancel}
	p.cache[key] = entry
	p.queued = append(p.queued, entry)
	if !p.workerRunning {
		p.workerRunning = true
		go p.work()
	}
	p.mu.Unlock()
	return PathPlan{State: PathPending}
}

func planLifetime(state PathState) time.Duration {
	if state == PathUnreachable {
		return 5 * time.Second
	}
	return time.Second
}

func (p *PathPlanner) removeLocked(entry *pathEntry) {
	delete(p.cache, entry.key)
	for i, queued := range p.queued {
		if queued == entry {
			p.queued = append(p.queued[:i], p.queued[i+1:]...)
			break
		}
	}
	entry.cancel()
}

func (p *PathPlanner) work() {
	for {
		p.mu.Lock()
		if p.closed || len(p.queued) == 0 {
			p.workerRunning = false
			p.mu.Unlock()
			return
		}
		chosen := 0
		for i := 1; i < len(p.queued); i++ {
			if p.queued[i].priority < p.queued[chosen].priority ||
				(p.queued[i].priority == p.queued[chosen].priority &&
					p.queued[i].requestedAt.Before(p.queued[chosen].requestedAt)) {
				chosen = i
			}
		}
		entry := p.queued[chosen]
		p.queued = append(p.queued[:chosen], p.queued[chosen+1:]...)
		if p.clock().Sub(entry.requestedAt) > time.Second {
			p.removeLocked(entry)
			p.mu.Unlock()
			continue
		}
		entry.running = true
		p.searchCount++
		p.mu.Unlock()
		points := func() (route []Waypoint) {
			defer func() {
				if recover() != nil {
					route = nil
				}
			}()
			return p.search(entry.ctx, entry.key.terrain, entry.from,
				Waypoint{entry.key.goalX, entry.key.goalY}, entry.avoid)
		}()
		plan := PathPlan{State: PathUnreachable}
		if len(points) > 0 && entry.ctx.Err() == nil {
			plan = PathPlan{State: PathReady, Route: append([]Waypoint(nil), points...)}
		}
		p.mu.Lock()
		entry.running = false
		if !p.closed && p.cache[entry.key] == entry {
			entry.plan = plan
			entry.completedAt = p.clock()
		}
		p.mu.Unlock()
	}
}

func (p *PathPlanner) Clear() {
	p.mu.Lock()
	for _, entry := range p.cache {
		entry.cancel()
	}
	p.cache = make(map[pathKey]*pathEntry)
	p.queued = nil
	p.mu.Unlock()
}

func (p *PathPlanner) Close() {
	p.mu.Lock()
	p.closed = true
	for _, entry := range p.cache {
		entry.cancel()
	}
	p.cache = make(map[pathKey]*pathEntry)
	p.queued = nil
	p.mu.Unlock()
}

func (p *PathPlanner) SearchCount() int {
	p.mu.Lock()
	defer p.mu.Unlock()
	return p.searchCount
}
