#!/usr/bin/env python3
"""
Validates a community model-capability submission against
docs/model-database/model-capabilities.schema.json before it's reviewed.

Intentionally dependency-free (stdlib only) so it runs in CI or on a
contributor's machine without `pip install jsonschema`. This is a hand-rolled
subset of JSON Schema validation covering only what the schema actually
uses (type, enum, const, pattern, minimum/maximum, minLength, additionalProperties,
required) -- not a general-purpose validator.

Usage:
    python validate_model_submission.py path/to/submission.json
    python validate_model_submission.py path/to/submission.json --existing-db ../../src/OmenCoreApp/Hardware/ModelCapabilityDatabase.cs
"""
import argparse
import json
import re
import sys
from pathlib import Path

SCHEMA_PATH = Path(__file__).parent / "model-capabilities.schema.json"


def load_json(path: Path):
    try:
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)
    except json.JSONDecodeError as e:
        print(f"ERROR: {path} is not valid JSON: {e}")
        sys.exit(1)
    except FileNotFoundError:
        print(f"ERROR: file not found: {path}")
        sys.exit(1)


def type_matches(value, expected_types):
    if isinstance(expected_types, str):
        expected_types = [expected_types]
    checks = {
        "string": lambda v: isinstance(v, str),
        "integer": lambda v: isinstance(v, int) and not isinstance(v, bool),
        "boolean": lambda v: isinstance(v, bool),
        "object": lambda v: isinstance(v, dict),
        "array": lambda v: isinstance(v, list),
        "null": lambda v: v is None,
    }
    return any(checks[t](value) for t in expected_types if t in checks)


def validate_node(value, node_schema, path, errors):
    if "type" in node_schema and not type_matches(value, node_schema["type"]):
        errors.append(f"{path}: expected type {node_schema['type']}, got {type(value).__name__}")
        return

    if "enum" in node_schema and value not in node_schema["enum"]:
        errors.append(f"{path}: value {value!r} not in allowed set {node_schema['enum']}")

    if "const" in node_schema and value != node_schema["const"]:
        errors.append(f"{path}: must be exactly {node_schema['const']!r}, got {value!r}")

    if isinstance(value, str):
        if "pattern" in node_schema and not re.match(node_schema["pattern"], value):
            errors.append(f"{path}: {value!r} does not match required pattern {node_schema['pattern']}")
        if "minLength" in node_schema and len(value) < node_schema["minLength"]:
            errors.append(f"{path}: must be at least {node_schema['minLength']} characters")

    if isinstance(value, int) and not isinstance(value, bool):
        if "minimum" in node_schema and value < node_schema["minimum"]:
            errors.append(f"{path}: {value} is below minimum {node_schema['minimum']}")
        if "maximum" in node_schema and value > node_schema["maximum"]:
            errors.append(f"{path}: {value} is above maximum {node_schema['maximum']}")

    if isinstance(value, dict) and "properties" in node_schema:
        allowed = set(node_schema["properties"].keys())
        if node_schema.get("additionalProperties") is False:
            for key in value:
                if key not in allowed:
                    errors.append(f"{path}: unexpected field '{key}' (not in schema)")
        for req in node_schema.get("required", []):
            if req not in value:
                errors.append(f"{path}: missing required field '{req}'")
        for key, subschema in node_schema["properties"].items():
            if key in value:
                validate_node(value[key], subschema, f"{path}.{key}", errors)


def semantic_checks(data, errors, warnings):
    fan = data.get("fanControl", {})
    if fan.get("supportsIndependentFanCurves") and not fan.get("supportsFanCurves", True):
        errors.append("fanControl: supportsIndependentFanCurves=true requires supportsFanCurves=true")

    if not fan.get("supportsFanControlWmi", True) and not fan.get("supportsFanControlEc", True):
        warnings.append(
            "fanControl: both supportsFanControlWmi and supportsFanControlEc are false -- "
            "this model would have no fan control path at all. Double-check this is really true."
        )

    lighting = data.get("lighting", {})
    if lighting.get("hasPerKeyRgb") and not lighting.get("hasKeyboardBacklight", True):
        errors.append("lighting: hasPerKeyRgb=true requires hasKeyboardBacklight=true")

    notes = data.get("notes")
    if notes and len(notes.strip()) < 10:
        warnings.append("notes: present but very short -- consider adding more detail for future readers")

    if not data.get("sourceDiagnosticsExport"):
        errors.append(
            "sourceDiagnosticsExport must be true -- submissions not backed by a real diagnostics "
            "export/Guided Fan Diagnostic run are not accepted (see CONTRIBUTING_MODEL_DATABASE.md)"
        )


def check_duplicate_product_id(product_id: str, db_path: Path, warnings):
    if not db_path or not db_path.exists():
        return
    text = db_path.read_text(encoding="utf-8", errors="ignore")
    if re.search(rf'ProductId\s*=\s*"{re.escape(product_id)}"', text, re.IGNORECASE):
        warnings.append(
            f"ProductId '{product_id}' already exists in {db_path.name} -- this submission may be an "
            "update to an existing entry rather than a new model. Say so explicitly in the PR description."
        )


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("submission", type=Path, help="Path to the submitted model JSON file")
    parser.add_argument(
        "--existing-db",
        type=Path,
        default=None,
        help="Path to ModelCapabilityDatabase.cs, to warn on likely duplicate ProductIds",
    )
    args = parser.parse_args()

    schema = load_json(SCHEMA_PATH)
    data = load_json(args.submission)

    errors: list[str] = []
    warnings: list[str] = []

    validate_node(data, schema, "$", errors)
    if not errors:
        semantic_checks(data, errors, warnings)
        if "productId" in data:
            check_duplicate_product_id(data["productId"], args.existing_db, warnings)

    if warnings:
        print("WARNINGS:")
        for w in warnings:
            print(f"  - {w}")
        print()

    if errors:
        print(f"FAILED: {len(errors)} error(s) in {args.submission}")
        for e in errors:
            print(f"  - {e}")
        sys.exit(1)

    print(f"OK: {args.submission} is a valid model capability submission.")
    sys.exit(0)


if __name__ == "__main__":
    main()
