package main

import (
	"encoding/json"
	"flag"
	"fmt"
	"os"

	"partypilot/internal/fpsconfig"
)

func main() {
	path := flag.String("config", "", "explicit PoE2 production config INI path")
	foregroundText := flag.String("foreground", "", "keep, unlimited/0, or a positive FPS number")
	backgroundText := flag.String("background", "", "keep, unlimited/0, or a positive FPS number")
	apply := flag.Bool("apply", false, "back up and update the config; close all game clients first")
	flag.Parse()
	if *foregroundText == "" || *backgroundText == "" {
		fatal(fmt.Errorf("both -foreground and -background are required"))
	}
	foreground, err := fpsconfig.ParseLimit(*foregroundText)
	if err != nil {
		fatal(err)
	}
	background, err := fpsconfig.ParseLimit(*backgroundText)
	if err != nil {
		fatal(err)
	}
	result, err := fpsconfig.Prepare(*path, fpsconfig.Policy{Foreground: foreground, Background: background}, *apply)
	if err != nil {
		fatal(err)
	}
	if err := json.NewEncoder(os.Stdout).Encode(result); err != nil {
		fatal(err)
	}
}

func fatal(err error) {
	fmt.Fprintln(os.Stderr, err)
	os.Exit(1)
}
