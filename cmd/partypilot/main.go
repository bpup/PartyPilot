package main

import (
	"bufio"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"os"
	"time"

	"partypilot/internal/follow"
	"partypilot/internal/navigation"
)

type frame struct {
	Now           time.Time        `json:"now"`
	Leader        follow.Position  `json:"leader"`
	Follower      follow.Position  `json:"follower"`
	Reset         bool             `json:"reset,omitempty"`
	Terrain       *navigation.Grid `json:"terrain,omitempty"`
	PackedTerrain *packedTerrain   `json:"packed_terrain,omitempty"`
}

// Packed terrain is accepted only in controlled replay. JSON byte slices use
// base64 encoding; the caller must supply independently checked coordinates.
type packedTerrain struct {
	Area        string  `json:"area"`
	OriginX     float64 `json:"origin_x"`
	OriginY     float64 `json:"origin_y"`
	CellSize    float64 `json:"cell_size"`
	BytesPerRow int     `json:"bytes_per_row"`
	Rows        int     `json:"rows"`
	Data        []byte  `json:"data"`
}

func replay(reader io.Reader, writer io.Writer, config follow.Config) error {
	engine, ok := follow.New(config)
	if !ok {
		return errors.New("invalid follow configuration")
	}
	scanner := bufio.NewScanner(reader)
	scanner.Buffer(make([]byte, 64*1024), 1024*1024)
	encoder := json.NewEncoder(writer)
	line := 0
	for scanner.Scan() {
		line++
		var item frame
		if err := json.Unmarshal(scanner.Bytes(), &item); err != nil {
			return fmt.Errorf("line %d: %w", line, err)
		}
		if item.Now.IsZero() {
			return fmt.Errorf("line %d: missing now", line)
		}
		if item.PackedTerrain != nil {
			if item.Terrain != nil {
				return fmt.Errorf("line %d: two terrain sources", line)
			}
			p := item.PackedTerrain
			var err error
			item.Terrain, err = navigation.GridFromPacked(p.Area, p.OriginX, p.OriginY,
				p.CellSize, p.Data, p.BytesPerRow, p.Rows)
			if err != nil {
				return fmt.Errorf("line %d: %w", line, err)
			}
		}
		if item.Reset {
			engine.Reset()
		}
		if err := encoder.Encode(engine.StepWithTerrain(item.Now, item.Leader, item.Follower, item.Terrain)); err != nil {
			return err
		}
	}
	return scanner.Err()
}

func main() {
	input := flag.String("replay", "", "JSONL observations to evaluate; '-' reads stdin")
	stop := flag.Float64("stop", -1, "stop distance in telemetry units")
	resume := flag.Float64("resume", -1, "resume distance in telemetry units")
	catchUp := flag.Float64("catchup", -1, "catch-up distance in telemetry units")
	maxAge := flag.Duration("max-age", 0, "maximum position age, such as 750ms")
	trailSpacing := flag.Float64("trail-spacing", 0, "leader trail sample spacing; requires -trail-max-step")
	trailMaxStep := flag.Float64("trail-max-step", 0, "maximum leader displacement between observations")
	requireTerrain := flag.Bool("require-terrain", false, "stop when an instance-matched terrain grid is unavailable")
	targetTrail := flag.Bool("target-trail", false, "use the target build's observed trail spacing rule")
	classicCollisionDodge := flag.Bool("classic-collision-dodge", false, "use controlled terrain to rearm classic sprint near obstacles")
	flag.Parse()
	config := follow.Config{StopDistance: *stop, ResumeDistance: *resume, CatchUpDistance: *catchUp, MaxAge: *maxAge,
		TrailSpacing: *trailSpacing, TrailMaxStep: *trailMaxStep, RequireTerrain: *requireTerrain,
		TargetTrail: *targetTrail, ClassicCollisionDodge: *classicCollisionDodge}
	if *input == "" || !config.Valid() {
		fmt.Fprintln(os.Stderr, "Usage: partypilot -replay observations.jsonl -stop N -resume N -catchup N -max-age 750ms")
		os.Exit(2)
	}
	var source io.Reader = os.Stdin
	if *input != "-" {
		file, err := os.Open(*input)
		if err != nil {
			fmt.Fprintln(os.Stderr, err)
			os.Exit(1)
		}
		defer file.Close()
		source = file
	}
	if err := replay(source, os.Stdout, config); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}
