#!/usr/bin/env bash
#
# Runs the Linux job of .github/workflows/ci.yml on a real machine - the same commands, in the same order - so a
# local iteration and a CI run mean the same thing. The job needs a .NET SDK (6.0 and 9.0; the smoke project also
# targets net10.0, so a .NET 10 SDK is required to restore it), PowerShell 7 and git. It needs no Docker, no
# Python and no desktop session: the Docker-mode cases are excluded, the Python samples are not part of CI, and
# the WPF end-to-end tests skip themselves outside Windows.
#
# Three deliberate differences from CI:
#
#   * CI stops at the first failing step. This script runs every step and reports all of them, because on a
#     machine of your own the whole picture is worth more than the first failure. The build phase is the
#     exception: every later step needs the build, so a build failure stops the run there.
#   * It writes TRX files exactly like CI, and when something failed it prints the same `::error` annotations
#     through scripts/emit-test-failures.ps1 - the mechanism that makes a failure readable without a job log.
#   * A filter that matches no test is an error here. `dotnet test` exits 0 in that case ("no test matches the
#     given testcase filter"), which would turn a typo in --filter into a green run.
#
# The Windows job (WPF samples, FlaUI UI automation) cannot be reproduced here; it needs Windows.
#
# Usage: scripts/ci-linux.sh [options]
#   -c, --configuration <cfg>   build configuration (default: Release)
#       --skip-build            reuse what is already built
#       --skip-smoke            skip the smoke tests (needs a .NET 10 SDK, see the warning this script prints)
#       --suite <name>          run one suite only: smoke, runner, remoterunner, library, bridge, coverage, contract
#       --filter <expr>         extra dotnet test --filter for that suite (requires --suite)
#       --no-annotations        do not print GitHub-style annotations at the end
#   -h, --help                  this text
#
# Example: run one failing-looking class on a Linux box
#   ./scripts/ci-linux.sh --skip-build --suite bridge --filter "FullyQualifiedName~McpRelayHostTests"
#
set -o pipefail

usage() {
    cat <<'EOF'
Runs the Linux job of .github/workflows/ci.yml locally, with the same commands in the same order.

Usage: scripts/ci-linux.sh [options]
  -c, --configuration <cfg>   build configuration (default: Release)
      --skip-build            reuse what is already built
      --skip-smoke            skip the smoke tests (needs a .NET 10 SDK, see the warning this script prints)
      --suite <name>          run one suite only: smoke, runner, remoterunner, library, bridge, coverage, contract
      --filter <expr>         extra dotnet test --filter for that suite (requires --suite)
      --no-annotations        do not print GitHub-style annotations at the end
  -h, --help                  this text

Prerequisites: a .NET SDK (6.0 and 9.0, plus 10.0 for the smoke project's net10.0 target), PowerShell 7 and git.
The Windows job - the WPF samples and the FlaUI UI automation - cannot be reproduced here; that needs Windows.
EOF
}

configuration=Release
skip_build=0
skip_smoke=0
suite=
filter=
annotations=1
results=TestResults

while [ $# -gt 0 ]; do
    case "$1" in
        -c|--configuration) configuration=${2:?missing value for $1}; shift 2 ;;
        --skip-build) skip_build=1; shift ;;
        --skip-smoke) skip_smoke=1; shift ;;
        --suite) suite=${2:?missing value for $1}; shift 2 ;;
        --filter) filter=${2:?missing value for $1}; shift 2 ;;
        --no-annotations) annotations=0; shift ;;
        -h|--help) usage; exit 0 ;;
        *) echo "unknown option: $1" >&2; usage >&2; exit 2 ;;
    esac
done

root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$root" || exit 2
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

echo "repository : $root"
echo "commit     : $(git log --oneline -1 2>/dev/null || echo 'not a git checkout')"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "dotnet is not on PATH; install a .NET SDK first (see docs/contributing)" >&2
    exit 2
fi
if ! command -v pwsh >/dev/null 2>&1; then
    echo "pwsh is not on PATH; the coverage floor and the packaged-contract check are PowerShell scripts" >&2
    exit 2
fi

echo "dotnet     : $(dotnet --version)"
echo "sdks       : $(dotnet --list-sdks | tr '\n' ' ')"
echo "runtimes   : $(dotnet --list-runtimes | grep -c '^Microsoft\.') installed"

# The three warnings below are the failures people actually hit on a fresh machine, and each one reads like a
# repository problem when it is not.
dotnet --list-sdks | grep -q '^6\.' || echo "warning: no .NET 6 SDK on PATH - the net6.0 smoke leg and the net6.0 build target will fail"
dotnet --list-sdks | grep -q '^9\.' || echo "warning: no .NET 9 SDK on PATH - the net9.0 targets will not build"
if ! dotnet --list-sdks | grep -q '^10\.'; then
    echo "warning: no .NET 10 SDK on PATH. tests/LibDemo.SmokeTests targets net6.0;net9.0;net10.0, so its restore"
    echo "         fails with NETSDK1045 even for --framework net6.0. CI's runner image ships a .NET 10 SDK, which"
    echo "         is why CI never sees this; here you can install one or pass --skip-smoke."
fi
dotnet --list-runtimes | grep -q '^Microsoft.NETCore.App 6\.' || echo "warning: no .NET 6 runtime on PATH - the net6.0 smoke tests will not run"

# The build list is the workflow's, in the workflow's order: the bridge samples are console applications and the
# remote-runner test starts its own sample as a second process, so all of them have to exist before the tests.
projects=(
    src/Ason.Abstractions/Ason.Abstractions.csproj
    src/Ason.Runner.Core/Ason.Runner.Core.csproj
    src/Ason/Ason.csproj
    src/Ason.ExternalExecutor/Ason.ExternalExecutor.csproj
    src/Ason.RemoteBridge/Ason.RemoteBridge.csproj
    src/Ason.Bridge/Ason.Bridge.csproj
    src/Ason.Bridge.Grpc/Ason.Bridge.Grpc.csproj
    src/Ason.Bridge.Mcp/Ason.Bridge.Mcp.csproj
    src/Ason.Bridge.OpenApi/Ason.Bridge.OpenApi.csproj
    src/Ason.Bridge.McpHost/Ason.Bridge.McpHost.csproj
    samples/LibDemo/LibDemo.csproj
    samples/ConsoleBridgeAppSample/ConsoleBridgeAppSample.csproj
    samples/ConsoleAgentSample/ConsoleAgentSample.csproj
    samples/ConsoleBridgeCallerSample/ConsoleBridgeCallerSample.csproj
    samples/RemoteRunnerService/RunnerServiceSample.csproj
)

failed=0
step_names=()
step_status=()

run_step() {
    local name=$1
    shift
    step_names+=("$name")
    printf '\n=== %s ===\n' "$name"
    if "$@"; then
        step_status+=("ok")
        printf -- '--- %s: ok\n' "$name"
    else
        step_status+=("FAILED")
        failed=$((failed + 1))
        printf -- '--- %s: FAILED\n' "$name"
    fi
}

test_project() {   # test_project <trx-name> <results-dir> <project> [dotnet test arguments...]
    local trx=$1 directory=$2 project=$3
    shift 3
    # `dotnet test` exits 0 when a filter matches no test at all ("no test matches the given testcase filter"),
    # which turns a typo in --filter into a green run. VSTest is told to treat that as an error instead; the
    # RunSettings property travels after `--`, which is what separates them from `dotnet test`'s own options.
    dotnet test "$project" \
        --configuration "$configuration" \
        --logger "trx;LogFileName=$trx" --results-directory "$directory" "$@" \
        -- RunConfiguration.TreatNoTestsAsError=true
}

filter_arguments=()
if [ -n "$filter" ]; then
    filter_arguments=(--filter "$filter")
fi

run_smoke() {
    test_project libdemo-net6.trx "$results" tests/LibDemo.SmokeTests/LibDemo.SmokeTests.csproj --framework net6.0 "${filter_arguments[@]}" \
        && test_project libdemo-net9.trx "$results" tests/LibDemo.SmokeTests/LibDemo.SmokeTests.csproj --framework net9.0 "${filter_arguments[@]}"
}

run_runner() {
    test_project ason-runner-tests.trx "$results" tests/Ason.Runner.Tests/Ason.Runner.Tests.csproj "${filter_arguments[@]}"
}

run_remoterunner() {
    test_project ason-remoterunner-tests.trx "$results" tests/Ason.RemoteRunner.Tests/Ason.RemoteRunner.Tests.csproj "${filter_arguments[@]}"
}

run_library() {
    # Docker-mode cases need a daemon and the MCP client tests need live servers, so CI excludes both.
    local hermetic='DisplayName!~Docker&FullyQualifiedName!~McpClientTests'
    if [ -n "$filter" ]; then
        hermetic="$hermetic&($filter)"
    fi
    test_project ason-tests.trx "$results" tests/Ason.Tests/Ason.Tests.csproj --filter "$hermetic"
}

run_bridge() {
    # Coverage is collected here because this suite always runs in full; the floor is checked by its own step.
    # Reports from earlier runs are removed first: the floor script reads every report it can find, so a stale
    # passing one would hide the regression this run was meant to catch. CI needs no cleanup (fresh runner); this
    # run showed the problem by reporting "floor met for 12 bridge package(s)" - three runs' worth of reports.
    rm -rf artifacts/coverage
    test_project ason-bridge-tests.trx artifacts/coverage tests/Ason.Bridge.Tests/Ason.Bridge.Tests.csproj \
        --collect:"XPlat Code Coverage" --settings coverlet.runsettings "${filter_arguments[@]}"
}

run_coverage_floor() {
    pwsh ./scripts/check-bridge-coverage.ps1 -CoverageFile 'artifacts/coverage/*/coverage.cobertura.xml'
}

run_contract() {
    pwsh ./scripts/check-package-contract.ps1
}

if [ "$skip_build" -eq 0 ]; then
    printf '\n=== build libraries and samples ===\n'
    for project in "${projects[@]}"; do
        printf -- '--- %s\n' "$project"
        if ! dotnet build "$project" --configuration "$configuration"; then
            echo "build failed: $project" >&2
            exit 1
        fi
    done
fi

if [ -n "$suite" ]; then
    case "$suite" in
        smoke) run_step "smoke tests (net6.0, net9.0)" run_smoke ;;
        runner) run_step "runner tests" run_runner ;;
        remoterunner) run_step "remote-runner tests" run_remoterunner ;;
        library) run_step "library tests (hermetic filter)" run_library ;;
        bridge) run_step "bridge tests (with coverage)" run_bridge ;;
        coverage) run_step "coverage floor (bridge adapters)" run_coverage_floor ;;
        contract) run_step "the contract ships with the package" run_contract ;;
        *) echo "unknown suite: $suite (expected smoke, runner, remoterunner, library, bridge, coverage, contract)" >&2; exit 2 ;;
    esac
else
    if [ "$skip_smoke" -eq 0 ]; then
        run_step "smoke tests (net6.0, net9.0)" run_smoke
    fi
    run_step "runner tests" run_runner
    run_step "remote-runner tests" run_remoterunner
    run_step "library tests (hermetic filter)" run_library
    run_step "bridge tests (with coverage)" run_bridge
    run_step "coverage floor (bridge adapters)" run_coverage_floor
    run_step "the contract ships with the package" run_contract
fi

printf '\n=== summary ===\n'
index=0
while [ "$index" -lt "${#step_names[@]}" ]; do
    printf '  %-6s %s\n' "${step_status[$index]}" "${step_names[$index]}"
    index=$((index + 1))
done

if [ "$failed" -gt 0 ] && [ "$annotations" -eq 1 ]; then
    printf '\n=== annotations (the ones CI would publish) ===\n'
    pwsh ./scripts/emit-test-failures.ps1 -ResultsDirectory "$results" artifacts/coverage artifacts || true
fi

if [ "$failed" -gt 0 ]; then
    printf '\n%d step(s) failed\n' "$failed" >&2
    exit 1
fi
printf '\nall steps passed\n'
