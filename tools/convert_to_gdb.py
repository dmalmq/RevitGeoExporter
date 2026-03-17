#!/usr/bin/env python3
"""Convert GeoPackage files (.gpkg) to Esri File Geodatabase (.gdb) using ogr2ogr."""

from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Iterable, List, Optional


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Convert one .gpkg file or a directory of .gpkg files to .gdb."
    )
    parser.add_argument(
        "input_path",
        type=Path,
        help="Input .gpkg file or directory containing .gpkg files.",
    )
    parser.add_argument(
        "output_path",
        nargs="?",
        type=Path,
        help=(
            "Optional output location. For single-file input this can be a .gdb path "
            "or a directory. For directory input this must be a directory."
        ),
    )
    parser.add_argument(
        "--recurse",
        action="store_true",
        help="When input_path is a directory, search subdirectories recursively.",
    )
    parser.add_argument(
        "--overwrite",
        action="store_true",
        help="Overwrite existing .gdb outputs.",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Print conversion commands without running them.",
    )
    parser.add_argument(
        "--ogr2ogr",
        default="ogr2ogr",
        help="Path to ogr2ogr executable (default: ogr2ogr from PATH).",
    )
    return parser


def is_gpkg(path: Path) -> bool:
    return path.suffix.lower() == ".gpkg"


def resolve_ogr2ogr(ogr2ogr_arg: str) -> Optional[str]:
    candidate = Path(ogr2ogr_arg)
    if candidate.exists():
        return str(candidate)
    return shutil.which(ogr2ogr_arg)


def gather_sources(input_path: Path, recurse: bool) -> List[Path]:
    if not input_path.exists():
        raise FileNotFoundError(f"Input path does not exist: {input_path}")

    if input_path.is_file():
        if not is_gpkg(input_path):
            raise ValueError(f"Input file must end with .gpkg: {input_path}")
        return [input_path]

    pattern = "**/*.gpkg" if recurse else "*.gpkg"
    sources = sorted(input_path.glob(pattern))
    if not sources:
        raise ValueError(f"No .gpkg files found in directory: {input_path}")
    return sources


def resolve_single_output(source: Path, output_path: Optional[Path]) -> Path:
    if output_path is None:
        return source.with_suffix(".gdb")

    if output_path.suffix.lower() == ".gdb":
        return output_path

    return output_path / f"{source.stem}.gdb"


def resolve_directory_output_root(
    input_dir: Path,
    output_path: Optional[Path],
) -> Path:
    if output_path is None:
        return input_dir / "gdb"

    if output_path.suffix.lower() == ".gdb":
        raise ValueError(
            "For directory input, output_path must be a directory, not a .gdb path."
        )

    return output_path


def compute_destination(
    source: Path,
    input_path: Path,
    output_path: Optional[Path],
) -> Path:
    if input_path.is_file():
        return resolve_single_output(source, output_path)

    output_root = resolve_directory_output_root(input_path, output_path)
    relative_parent = source.relative_to(input_path).parent
    return output_root / relative_parent / f"{source.stem}.gdb"


def format_command(command: Iterable[str]) -> str:
    return " ".join(f'"{part}"' if " " in part else part for part in command)


def run_conversion(
    ogr2ogr: str,
    source: Path,
    destination: Path,
    overwrite: bool,
    dry_run: bool,
) -> None:
    destination_parent = destination.parent
    if not dry_run:
        destination_parent.mkdir(parents=True, exist_ok=True)

    if destination.exists():
        if not overwrite:
            print(f"Skipping (exists): {destination}")
            return
        if not dry_run:
            shutil.rmtree(destination)

    command = [ogr2ogr, "-f", "OpenFileGDB", str(destination), str(source)]
    print(f"{'Would run' if dry_run else 'Running'}: {format_command(command)}")

    if dry_run:
        return

    result = subprocess.run(
        command,
        check=False,
        capture_output=True,
        text=True,
    )
    if result.returncode != 0:
        stderr = result.stderr.strip()
        if not stderr:
            stderr = "ogr2ogr failed with no stderr output."
        raise RuntimeError(f"Failed converting {source.name}: {stderr}")

    print(f"Converted: {source.name} -> {destination.name}")


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()

    try:
        sources = gather_sources(args.input_path, args.recurse)
    except (FileNotFoundError, ValueError) as exc:
        parser.error(str(exc))

    ogr2ogr_path = resolve_ogr2ogr(args.ogr2ogr)
    if ogr2ogr_path is None:
        print(
            "Error: ogr2ogr was not found. Install GDAL 3.6+ and make sure "
            "ogr2ogr is on PATH, or pass --ogr2ogr with an explicit executable path.",
            file=sys.stderr,
        )
        return 2

    success_count = 0
    failures: List[str] = []

    for source in sources:
        destination = compute_destination(source, args.input_path, args.output_path)
        try:
            run_conversion(
                ogr2ogr=ogr2ogr_path,
                source=source,
                destination=destination,
                overwrite=args.overwrite,
                dry_run=args.dry_run,
            )
            success_count += 1
        except Exception as exc:  # noqa: BLE001
            failures.append(str(exc))

    print(f"Done. Success: {success_count}, Failed: {len(failures)}")
    if failures:
        for failure in failures:
            print(f"  - {failure}", file=sys.stderr)
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
