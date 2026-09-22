#!/usr/bin/env python3
# -*- coding: utf-8 -*-

import argparse
import csv
from pathlib import Path


def compute_2back_targets_on_changes(rows, sec_task_idx, header_len):
    """
    Logik:
    - "Wert zählt" nur, wenn er sich im Vergleich zur Vorzeile ändert (Run-Wechsel).
    - 2-back bezieht sich auf die Sequenz der unterschiedlichen Werte (Run-Starts).
    - Wenn ein Run ein Target ist, bekommen ALLE Zeilen dieses Runs eine 1, sonst 0.
    """
    out_rows = []
    last_value = None

    # Buffer der letzten zwei *distinct* Werte (wie in C# Check2Back)
    distinct_buffer = []  # hält max 2 Werte

    current_run_is_target = "0"

    for r in rows:
        # Zeile auf Header-Länge auffüllen, damit Indizes immer passen
        if len(r) < header_len:
            r = r + [""] * (header_len - len(r))

        # Falls sec_task_idx trotzdem außerhalb liegt (sehr kaputte Zeile), kein Target
        if sec_task_idx >= len(r):
            out_rows.append((r, "0"))
            continue

        v_raw = r[sec_task_idx].strip()

        # Wenn leer/kaputt: konservativ kein Target, Run-Logik nicht updaten
        if v_raw == "":
            out_rows.append((r, "0"))
            continue

        # SecTaskNum sind normalerweise ints; sonst als string vergleichen
        try:
            v = int(v_raw)
        except ValueError:
            v = v_raw

        if last_value is None:
            # erster Run
            current_run_is_target = "0"

            # distinct buffer updaten (als distinct event)
            distinct_buffer.append(v)
            if len(distinct_buffer) > 2:
                distinct_buffer = distinct_buffer[-2:]

            last_value = v
        else:
            if v != last_value:
                # Run-Wechsel => neuer distinct event

                # 2-back check: wenn wir bereits 2 distinct Werte haben
                if len(distinct_buffer) >= 2:
                    two_back = distinct_buffer[0]
                    current_run_is_target = "1" if v == two_back else "0"
                else:
                    current_run_is_target = "0"

                # buffer weiter schieben
                distinct_buffer.append(v)
                if len(distinct_buffer) > 2:
                    distinct_buffer = distinct_buffer[-2:]

                last_value = v
            # else: gleicher Wert => gleicher Run => Target-Status bleibt

        out_rows.append((r, current_run_is_target))

    return out_rows


def process_file(in_path: Path, out_path: Path):
    # Roh lesen, damit wir Format (Delimiter, Dezimal-Kommas, etc.) nicht "verschlimmbessern"
    with in_path.open("r", encoding="utf-8", newline="") as f:
        lines = f.readlines()

    # Kommentarzeilen am Anfang behalten (z.B. "# ...")
    comment_lines = []
    i = 0
    while i < len(lines) and lines[i].startswith("#"):
        comment_lines.append(lines[i])
        i += 1

    if i >= len(lines):
        raise RuntimeError(f"Keine Header-Zeile gefunden in {in_path.name}")

    header_line = lines[i].rstrip("\n")
    i += 1

    # Header parsen (Delimiter ;)
    header = next(csv.reader([header_line], delimiter=";"))

    if "SecTaskNum" not in header:
        raise RuntimeError(f"Spalte 'SecTaskNum' nicht gefunden in {in_path.name}")

    sec_idx = header.index("SecTaskNum")
    insert_idx = sec_idx + 1

    new_header = header[:insert_idx] + ["SecTaskIsTarget"] + header[insert_idx:]
    header_len = len(header)

    # Datenzeilen einlesen (Delimiter ;)
    # Leere Zeilen am Ende ignorieren; sonstige Zeilen bleiben
    data_lines = [ln.rstrip("\n") for ln in lines[i:] if ln.strip() != ""]
    reader = csv.reader(data_lines, delimiter=";")
    rows = [row for row in reader]

    # Targets berechnen
    marked = compute_2back_targets_on_changes(rows, sec_idx, header_len)

    # Output schreiben (Kommentar + neuer Header + Daten)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    with out_path.open("w", encoding="utf-8", newline="") as f:
        for cl in comment_lines:
            f.write(cl if cl.endswith("\n") else cl + "\n")

        w = csv.writer(f, delimiter=";", lineterminator="\n", quoting=csv.QUOTE_MINIMAL)

        w.writerow(new_header)

        for row, is_target in marked:
            # Sicherstellen, dass Zeile mindestens Header-Länge hat
            if len(row) < header_len:
                row = row + [""] * (header_len - len(row))

            new_row = row[:insert_idx] + [is_target] + row[insert_idx:]
            w.writerow(new_row)


def main():
    ap = argparse.ArgumentParser(
        description="Fügt SecTaskIsTarget (2-back, nur bei Wertwechsel) in 5_*.csv Dateien ein."
    )
    ap.add_argument("input_dir", help="Ordner mit den CSV-Logs")
    ap.add_argument("output_dir", help="Zielordner für Output-CSVs")
    args = ap.parse_args()

    in_dir = Path(args.input_dir)
    out_dir = Path(args.output_dir)

    if not in_dir.exists() or not in_dir.is_dir():
        raise SystemExit(f"Input-Ordner existiert nicht oder ist kein Ordner: {in_dir}")

    out_dir.mkdir(parents=True, exist_ok=True)

    files = sorted(
        [p for p in in_dir.iterdir()
         if p.is_file() and p.name.startswith("5_") and p.suffix.lower() == ".csv"]
    )

    if not files:
        print("Keine Dateien gefunden, die mit '5_' beginnen und auf .csv enden.")
        return

    for p in files:
        out_path = out_dir / p.name
        process_file(p, out_path)
        print(f"OK: {p.name} -> {out_path}")

    print("Fertig.")


if __name__ == "__main__":
    main()
