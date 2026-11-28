# denice
The Denice AIOS Engine

The `denice` executable for macOS (arm64, x64), Windows (x64, arm64) and Linux (arm64, x64). `denice version` prints
the version it was built with.

## Running alpha builds

Alpha releases are signed with a self-signed certificate, so macOS Gatekeeper and Windows SmartScreen warn on
first launch. See [Running the alpha (self-signed) builds](docs/running-alpha-builds.md) for the one-time steps
to trust and run them on macOS, Windows, and Linux.

## Branches and what each one runs

Work lands on `working`, the default branch, and is promoted upward. Each branch adds GPU validation for the platforms
its audience needs:

| Branch | Audience | GPU validation |
|---|---|---|
| `main` | public releases | all GPU platforms |
| `dev` | beta releases | Windows GPU |
| `fast` | internal releases | Linux GPU |
| `working` | default branch, quick merges | none |

Test coverage must reach 75% independently for each of the six OS and CPU targets: macOS x64, macOS arm64, Windows
x64, Windows arm64, Linux x64 and Linux arm64. Runs combine only within the same target and the same source; coverage
from one target never counts toward another. A target a branch does not run on GPU is reported as not evaluated
there, never as passed.
