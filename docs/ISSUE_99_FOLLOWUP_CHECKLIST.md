# Issue 99 Follow-Up Checklist (Board 8E41)

Use this as a ready-to-post request in GitHub issue #99 to collect final validation evidence after the 8E41 safety-mapping fix.

## Comment template

Thanks for reporting this. We added board 8E41 to the same Transcend 14 safety path as 8C58 so legacy EC writes are blocked and diagnostics are now aligned.

If possible, run the bundled collector first so we get a complete report in one file:

```bash
./qa/collect-linux-triage.sh
```

Then attach the generated report file (`omencore-linux-triage.txt`) and optional `acpidump.dat`.

Could you run the following on your updated build and paste outputs?

```bash
uname -a
cat /etc/os-release
./omencore-cli --version
./omencore-cli status
./omencore-cli diagnose
./omencore-cli diagnose --report
```

And please include these interface checks:

```bash
ls -la /sys/devices/platform/hp-wmi/
ls -la /sys/devices/platform/hp-wmi/hwmon/*/ 2>/dev/null
cat /sys/class/dmi/id/board_name
cat /sys/class/dmi/id/product_name
```

Optional but very helpful:

```bash
sudo acpidump > acpidump-8e41.dat
```

What we are specifically verifying:
1. Capability class/reason text for 8E41 now reports the Transcend unsafe-EC path.
2. Diagnose recommendations are actionable for missing thermal_profile/fan target interfaces.
3. hp-wmi/hwmon exposure on your kernel can be categorized as profile-only vs telemetry-only.

If you can share all of that, we can finalize model-specific guidance quickly.

## Internal expected outcome
- `diagnose` should now include board-aware Transcend safety guidance for 8E41.
- No legacy EC write path should be used for 8E41.
- Capability classification should remain explicit even when fan control nodes are missing.

## Maintainer response templates

### Initial follow-up template

Thanks for the report and detailed hardware info. We shipped a fix that maps board 8E41 to the same Transcend 14 safety path as 8C58.

Please run:

```bash
./qa/collect-linux-triage.sh
```

Then attach:
1. `omencore-linux-triage.txt`
2. `acpidump.dat` (if generated)

This will let us confirm capability classification and kernel hp-wmi interface exposure on your exact setup.

### Post-evidence response template

Thanks, this is exactly what we needed.

From your logs we can see:
1. Board/model detection
2. Current capability class and reason
3. Which hp-wmi/hwmon interfaces are exposed by your kernel

Next action from our side:
- If fan target/profile nodes are missing, we will track this as kernel/firmware exposure gap and provide a compatibility note.
- If nodes are present but control fails, we will add model/board-specific handling in OmenCore.
