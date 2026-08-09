# `8d87/integration` — a merge product, not a working branch

This branch exists so the 8D87 work can be **built and run as a whole**. Every change on it lives on
a feature branch that is reviewed and merged on its own. Nothing originates here.

It exists only on the `tempestnano` fork. It is never proposed upstream.

## What is merged in

| Branch | Upstream PR | Chain |
|---|---|---|
| `tooling/smu-probe` | [#160](https://github.com/theantipopau/omencore/pull/160) | base of the SMU chain |
| `fix/amd-smu-transport` | [#161](https://github.com/theantipopau/omencore/pull/161) | on #160 |
| `amd/smu-power-limits` | unfiled — stacks on #161 | tip of the SMU chain |
| `8d87/profile-and-telemetry` | [#162](https://github.com/theantipopau/omencore/pull/162) | base of the 8D87 chain |
| `8d87/per-key-rgb` | unfiled — stacks on #162 | middle of the 8D87 chain |
| `8d87/lighting` | [#165](https://github.com/theantipopau/omencore/pull/165) | tip of the 8D87 chain |
| `8d87/adapter-power-override` | unfiled — stacks on #162 **and** #166 | second branch off the 8D87 base |
| `fix/quiet-test-notifications` | [#164](https://github.com/theantipopau/omencore/pull/164) | standalone |
| `fix/dgpu-rtd3-polling` | [#166](https://github.com/theantipopau/omencore/pull/166) | standalone, and a base of the override |
| `fix/worker-shutdown` | [#167](https://github.com/theantipopau/omencore/pull/167) | standalone |
| `fix/gpu-counter-dispose-race` | unfiled | standalone |
| `8d87/integration-scaffolding` | never filed — see below | this file, the CI workflow, `Open-AdapterOverridePr.ps1` |

`8d87/integration-scaffolding` is the odd one: it holds no product code, only the three files that
exist for this branch and nowhere else. It is merged like any other branch **because the alternative
was a manual step, and the manual step failed twice.** The rebuild recipe merges feature branches
and nothing else, so files carried by no branch are dropped every time it runs — silently, because
losing the workflow means the next push queues no run to notice the loss with. The second time, a
release asset went out that had been built by hand on a scratch publish, with a loose `drivers/`
folder and an uncompressed 202 MB exe: exactly the build the workflow's own guard rejects, published
because the guard was not there to run. Edit these three files on the scaffolding branch, never on
`8d87/integration`.

The chain tips, the standalone fixes, and `8d87/profile-and-telemetry` are merged directly. That
last one is also an ancestor of `8d87/lighting`, and it is still listed explicitly because it now
carries commits the lighting chain branched before — merging only the tip would silently drop them.

`8d87/adapter-power-override` has two bases, which is why it merges `fix/dgpu-rtd3-polling` rather
than sitting beside it. The override restarts the dGPU, and every process holding an NVML handle
across that restart dies where it stands; the guard against it extends the same "do not touch the
card right now" predicate that the RTD3 fix introduced, asked by the same two poll loops. Growing a
second predicate beside the first would have been the alternative, and a worse one. It cannot be
rebased onto both bases, so it merges one. `fix/dgpu-rtd3-polling` therefore reports **Already up to
date** during the rebuild below — that is correct, and it stays in the recipe so the branch is still
merged if the override is ever dropped from it.

## Two rules

1. **Never commit to this branch, and never open a pull request from it.** A fix belongs on the
   feature branch that owns the code, so that it reaches review and reaches upstream. A commit made
   here is invisible to every PR and is lost the next time the branch is rebuilt.
2. **Rebuild it, do not maintain it.** When a PR merges upstream, delete the branch and re-merge from
   the new `main`. Do not merge `main` into it — that accumulates a history nobody reads and hides
   whether the feature branches still apply cleanly.

## Rebuild

```sh
git fetch origin
git branch -D 8d87/integration
git switch -c 8d87/integration origin/main
git merge --no-ff 8d87/integration-scaffolding
git merge --no-ff amd/smu-power-limits
git merge --no-ff 8d87/lighting
git merge --no-ff 8d87/profile-and-telemetry
git merge --no-ff 8d87/adapter-power-override
git merge --no-ff fix/quiet-test-notifications
git merge --no-ff fix/dgpu-rtd3-polling
git merge --no-ff fix/worker-shutdown
git merge --no-ff fix/gpu-counter-dispose-race
git push --force-with-lease fork 8d87/integration
```

The scaffolding merge is first so that the branch has its CI before it has any code to run it on. It
is based on `origin/main` like the integration branch itself, so it merges without
`--allow-unrelated-histories` and cannot conflict with product code — the three files it carries are
touched by nothing else.

A conflict during that rebuild is the signal this branch is for. It means two feature branches have
diverged, or one no longer applies to `main`, and you have found out before a maintainer did.

## CI

`.github/workflows/8d87-integration.yml` runs on every push here, and covers four things upstream CI
does not:

- **The full test suite**, unfiltered.
- **The probe tools** — `tools/SmuProbe`, `tools/LightingProbe` and `tools/ViewProbe` are not in
  `OmenCore.sln`, so no other build compiles them.
- **A Release build**, which is what the installer ships.
- **A runnable build, uploaded as an artifact.** Findings on this hardware are confirmed by a person
  looking at the machine, so the build being tested has to be the one CI compiled rather than whatever
  a local tree happens to hold.

This matters more than it looks: upstream CI reports `action_required` on all of these pull requests,
because fork pull requests need a maintainer to approve each workflow run. Until one does, this branch
is the only automated build any of this work gets.

### The artifact, and the release

`omencore-8d87-<sha>-win-x64`, on the `Release build` job, kept 14 days. Named by SHA because this
branch is rebuilt rather than advanced — there is no "latest" a download can be trusted to be.

The same build also goes out as a **pre-release on the `8d87-integration` tag**, zipped, so there is a
download that does not expire and does not need a GitHub login. The tag is deleted and recreated on
every push, which is the only way a rebuilt branch can have a stable download URL; the commit it was
built from is in the release title and body, because the tag on its own identifies nothing.

That step is guarded by `github.repository != 'theantipopau/omencore'`. Everything else in this
workflow is invisible upstream if the file ever lands on a default branch, but a release is not — so
the guard is by repository rather than by branch.

Publishes what `build-installer.ps1` publishes, minus Inno Setup, and then checks three things — each
of which fails quietly rather than loudly, which is why they are asserted rather than eyeballed:

- **The hardware worker is a second executable.** `OmenCore.HardwareWorker.exe` is published beside
  the app, which looks for it next to itself and falls back to in-process monitoring when it is
  absent. Ship only `OmenCore.exe` and you get a working app that is quietly not the shipping one.
- **The exe is self-contained, not a framework-dependent apphost.** Both are called `OmenCore.exe`;
  the apphost is ~150 KB and needs a runtime installed. Size is what separates them.
- **`drivers/` is embedded, not beside the exe.** `IncludeAllContentForSelfExtract` puts the kernel
  driver blobs — `RyzenSMU.bin` among them, which the SMU chain changes — inside the bundle, and the
  app extracts them at run time. So there is no folder to look for, and every driver include in
  `OmenCoreApp.csproj` is `Condition="Exists(...)"`, meaning a blob missing from a checkout is dropped
  silently. The check reads the bundle manifest for `drivers/<name>` **with the directory component**:
  LibreHardwareMonitor carries managed resources named `...Resources.PawnIo.RyzenSMU.bin`, so a
  bare-filename search matches in every build and can never fail.

`EnableCompressionInSingleFile` takes the app from 202 MB to 85 MB and is why the whole upload is
~160 MB rather than ~275 MB.

### Running it

Push is the only live trigger. `workflow_dispatch` is declared but does nothing: GitHub offers manual
dispatch only for workflows that exist on the **default** branch, and this one exists on
`8d87/integration` alone, so `gh workflow run` answers `404 not found on the default branch`. Re-run
with `gh run rerun <id>` — add `--failed` to retry only the jobs that failed — or the web UI.

A `Set up job` failure reading `Failed to resolve action download info` is a GitHub incident, not a
defect here. It happens while the runner fetches `actions/checkout`, before any of this repo is
compiled, and it is worth recognising because it fails several jobs at once and looks like a real
break. `gh run rerun <id> --failed` is the whole fix.

### After rebuilding the fork

Actions are off by default on a fresh fork, and the workflow list reads `total_count: 0` until they
are enabled — no run will ever queue. `GET /actions/permissions` is not the check for this; it
reports the policy and can say `enabled: true` while the fork opt-in is still off. Enable it with:

```sh
gh api -X PUT repos/<owner>/omencore/actions/permissions -F enabled=true -f allowed_actions=all
```

`-F` sends a real boolean. `-f enabled=true` sends the string `"true"` and fails with HTTP 422.

## Hardware findings

Reverse-engineering notes, measurements and evidence for board 8D87 are not kept in this repo. They
live in the investigation tree, which is the source for the plan document under `docs/`.
