#!/usr/bin/env python3
"""Reference implementation of the ToggleForge rollout algorithm v1.

This script is an oracle independent from the C# code: it computes the expected
bucket of each test vector using only the Python standard library and writes them
to rollout-v1.json. The C# tests assert that their implementation agrees.

Specification: docs/rollout-algorithm.md. Never edit the JSON by hand.

Usage:
    python3 tools/rollout-vectors/generate.py          # rewrite the vectors file
    python3 tools/rollout-vectors/generate.py --check  # fail if the file is stale
"""

import argparse
import hashlib
import json
import sys
import unicodedata
from pathlib import Path

ALGORITHM_TAG = "toggleforge.rollout.v1"
BUCKET_COUNT = 10_000
LENGTH_PREFIX_SIZE = 4
HASH_PREFIX_SIZE = 8

REPO_ROOT = Path(__file__).resolve().parents[2]
VECTORS_PATH = REPO_ROOT / "tests" / "ToggleForge.Evaluation.Tests" / "Vectors" / "rollout-v1.json"

PLAYER_ID_MAX_LENGTH = 256
SEED_MAX_LENGTH = 64

MAX_KEY = "k" * 64
MAX_PLAYER_ID = "p" * PLAYER_ID_MAX_LENGTH
MAX_SEED = "s" * SEED_MAX_LENGTH

# Player ids and seeds share one rule (docs/rollout-algorithm.md). Candidates are UTF-16 code units
# because some of them (unpaired surrogates) cannot be represented as valid strings in JSON.
OPAQUE_TEXT_CANDIDATES: list[tuple[str, list[int]]] = [
    ("empty", []),
    ("single letter", [ord("a")]),
    ("ascii space only", [0x20]),
    ("ideographic space only", [0x3000]),
    ("no-break space only", [0xA0]),
    ("line separator only", [0x2028]),
    ("tab only", [0x09]),
    ("padded", [ord(c) for c in "  padded  "]),
    ("accented", [ord(c) for c in "Jogador Ação"]),
    ("emoji surrogate pair", [ord("p"), 0xD83C, 0xDFAE]),
    ("nul", [ord("p"), 0x00]),
    ("line feed", [ord("p"), 0x0A, ord("q")]),
    ("escape", [0x1B, ord("["), ord("3"), ord("1"), ord("m")]),
    ("delete", [ord("p"), 0x7F]),
    ("next line", [ord("p"), 0x85]),
    ("lone high surrogate", [ord("p"), 0xD800]),
    ("lone low surrogate", [0xDC00, ord("p")]),
    ("reversed surrogate pair", [ord("p"), 0xDE00, 0xD83C]),
    ("64 code units", [ord("x")] * 64),
    ("65 code units", [ord("x")] * 65),
    ("256 code units", [ord("x")] * 256),
    ("257 code units", [ord("x")] * 257),
]


def bucket(project_key: str, environment_key: str, flag_key: str, seed: str, player_id: str) -> int:
    fields = (ALGORITHM_TAG, project_key, environment_key, flag_key, seed, player_id)
    digest = hashlib.sha256(encode_canonically(fields)).digest()
    return int.from_bytes(digest[:HASH_PREFIX_SIZE], "big") % BUCKET_COUNT


def encode_canonically(fields: tuple[str, ...]) -> bytes:
    """Length-prefixes every field so that ("ab", "c") and ("a", "bc") never collide."""
    encoded = bytearray()
    for field in fields:
        data = field.encode("utf-8")
        encoded += len(data).to_bytes(LENGTH_PREFIX_SIZE, "big")
        encoded += data
    return bytes(encoded)


def is_valid_opaque_text(code_units: list[int], max_length: int) -> bool:
    """1..max_length UTF-16 code units, well-formed, no control character (Cc), not only separators (Z*)."""
    if not 0 < len(code_units) <= max_length or not is_well_formed_utf16(code_units):
        return False
    text = decode_utf16(code_units)
    if any(unicodedata.category(c) == "Cc" for c in text):
        return False
    return any(not unicodedata.category(c).startswith("Z") for c in text)


def is_well_formed_utf16(code_units: list[int]) -> bool:
    index = 0
    while index < len(code_units):
        unit = code_units[index]
        if 0xDC00 <= unit <= 0xDFFF:
            return False
        if 0xD800 <= unit <= 0xDBFF:
            has_low_surrogate = index + 1 < len(code_units) and 0xDC00 <= code_units[index + 1] <= 0xDFFF
            if not has_low_surrogate:
                return False
            index += 1
        index += 1
    return True


def decode_utf16(code_units: list[int]) -> str:
    return b"".join(unit.to_bytes(2, "big") for unit in code_units).decode("utf-16-be")


def encode_utf16(text: str) -> list[int]:
    data = text.encode("utf-16-be")
    return [int.from_bytes(data[i : i + 2], "big") for i in range(0, len(data), 2)]


def vector_inputs() -> list[tuple[str, str, str, str, str]]:
    base = ("studio-game", "prod", "new-shop", "seed-1")
    sequential_players = [(*base, f"player-{n}") for n in range(1, 11)]
    return [
        *sequential_players,
        ("studio-game", "staging", "new-shop", "seed-1", "player-1"),
        ("studio-game", "prod", "new-shop", "seed-2", "player-1"),
        ("studio-game", "prod", "battle.pass_v2", "seed-1", "player-1"),
        ("other-game", "prod", "new-shop", "seed-1", "player-1"),
        # Same concatenation, different field boundaries.
        ("ab", "c", "flag", "seed", "player"),
        ("a", "bc", "flag", "seed", "player"),
        # Unicode is hashed as UTF-8 bytes, without normalisation.
        ("studio-game", "prod", "new-shop", "seed-1", "Jogador Ação"),
        ("studio-game", "prod", "new-shop", "seed-1", "Jogador Ação"),
        ("studio-game", "prod", "new-shop", "seed-1", "プレイヤー"),
        ("studio-game", "prod", "new-shop", "seed-1", "gamer 🎮"),
        ("studio-game", "prod", "new-shop", "seed-1", "  padded  "),
        ("studio-game", "prod", "new-shop", "seed-1", "550e8400-e29b-41d4-a716-446655440000"),
        ("studio-game", "prod", "new-shop", "seed-1", "PLAYER-1"),
        ("a", "b", "c", "d", "e"),
        (MAX_KEY, MAX_KEY, MAX_KEY, MAX_SEED, MAX_PLAYER_ID),
        ("studio-game", "prod", "new-shop", "semente-ação-🎲", "player-1"),
    ]


def build_document() -> dict:
    for *_, seed, player_id in vector_inputs():
        assert is_valid_opaque_text(encode_utf16(seed), SEED_MAX_LENGTH), f"invalid seed in vectors: {seed!r}"
        assert is_valid_opaque_text(encode_utf16(player_id), PLAYER_ID_MAX_LENGTH), f"invalid player id: {player_id!r}"

    vectors = [
        {
            "projectKey": project_key,
            "environmentKey": environment_key,
            "flagKey": flag_key,
            "seed": seed,
            "playerId": player_id,
            "bucket": bucket(project_key, environment_key, flag_key, seed, player_id),
        }
        for project_key, environment_key, flag_key, seed, player_id in vector_inputs()
    ]
    return {
        "algorithm": ALGORITHM_TAG,
        "bucketCount": BUCKET_COUNT,
        "vectors": vectors,
        "playerIdCases": classify(PLAYER_ID_MAX_LENGTH),
        "seedCases": classify(SEED_MAX_LENGTH),
    }


def classify(max_length: int) -> list[dict]:
    return [
        {"name": name, "codeUnits": code_units, "valid": is_valid_opaque_text(code_units, max_length)}
        for name, code_units in OPAQUE_TEXT_CANDIDATES
    ]


def render(document: dict) -> str:
    return json.dumps(document, ensure_ascii=False, indent=1) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--check", action="store_true", help="fail if the committed vectors are stale")
    args = parser.parse_args()

    expected = render(build_document())
    if args.check:
        actual = VECTORS_PATH.read_text(encoding="utf-8") if VECTORS_PATH.exists() else ""
        if actual != expected:
            print(f"{VECTORS_PATH.relative_to(REPO_ROOT)} is stale; run tools/rollout-vectors/generate.py", file=sys.stderr)
            return 1
        print("rollout vectors are up to date")
        return 0

    VECTORS_PATH.write_text(expected, encoding="utf-8")
    print(f"wrote {len(vector_inputs())} vectors to {VECTORS_PATH.relative_to(REPO_ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
