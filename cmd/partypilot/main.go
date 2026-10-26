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
)

type frame struct {
	Now      time.Time       `json:"now"`
	Leader   follow.Position `json:"leader"`
	Follower follow.Position `json:"follower"`
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
		if err := encoder.Encode(engine.Step(item.Now, item.Leader, item.Follower)); err != nil {
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
	flag.Parse()
	config := follow.Config{StopDistance: *stop, ResumeDistance: *resume, CatchUpDistance: *catchUp, MaxAge: *maxAge}
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
