#!/usr/bin/env python3
# -*- coding: utf-8 -*-

import argparse
import csv
from pathlib import Path
from typing import List, Tuple, Optional


WINDOW_SECONDS = 1.5  # 1500 ms = 1.5 s


def parse_float_comma(s: str) -> Optional[float]:
    s = (s or "").strip()
    if s == "":
        return None
    s = s.replace(",", ".")
    try:
        return float(s)
    except ValueError:
        return None


def is_pressed_value(s: str) -> bool:
    v = (s or "").strip().lower()
    if v in ("1", "true", "t", "yes", "y"):
        return True
    if v in ("0", "false", "f", "no", "n", ""):
        return False
    try:
        return float(v.replace(",", ".")) != 0.0
    except ValueError:
        return False


def compute_2back_targets_on_changes(rows: List[List[str]], sec_task_idx: int, header_len: int) -> List[Tuple[List[str], str]]:
    out_rows = []
    last_value = None
    distinct_buffer = []  # max 2
    current_run_is_target = "0"

    for r in rows:
        if len(r) < header_len:
            r = r + [""] * (header_len - len(r))

        if sec_task_idx >= len(r):
            out_rows.append((r, "0"))
            continue

        v_raw = (r[sec_task_idx] or "").strip()
        if v_raw == "":
            out_rows.append((r, "0"))
            continue

        try:
            v = int(v_raw)
        except ValueError:
            v = v_raw

        if last_value is None:
            current_run_is_target = "0"
            distinct_buffer.append(v)
            if len(distinct_buffer) > 2:
                distinct_buffer = distinct_buffer[-2:]
            last_value = v
        else:
            if v != last_value:
                if len(distinct_buffer) >= 2:
                    two_back = distinct_buffer[0]
                    current_run_is_target = "1" if v == two_back else "0"
                else:
                    current_run_is_target = "0"

                distinct_buffer.append(v)
                if len(distinct_buffer) > 2:
                    distinct_buffer = distinct_buffer[-2:]

                last_value = v

        out_rows.append((r, current_run_is_target))

    return out_rows


def extract_target_windows(timestamps: List[Optional[float]], is_target: List[int]) -> List[Tuple[float, float]]:
    windows = []
    in_run = False
    onset = None

    for t, it in zip(timestamps, is_target):
        if t is None:
            continue

        if it == 1 and not in_run:
            in_run = True
            onset = t
        elif it == 0 and in_run:
            if onset is not None:
                windows.append((onset, onset + WINDOW_SECONDS))
            in_run = False
            onset = None

    if in_run and onset is not None:
        windows.append((onset, onset + WINDOW_SECONDS))

    return windows


def assign_presses_to_targets(press_times: List[float], windows: List[Tuple[float, float]]) -> Tuple[List[float], int, int]:
    press_times_sorted = sorted(press_times)
    windows_sorted = sorted(windows, key=lambda x: x[0])

    used_press = [False] * len(press_times_sorted)
    rts = []
    extra_in_window = 0

    # pro Window: erster unbenutzter Press im Window ist Hit
    for onset, end in windows_sorted:
        hit_idx = None
        for i, pt in enumerate(press_times_sorted):
            if used_press[i]:
                continue
            if pt < onset:
                continue
            if pt > end:
                break
            hit_idx = i
            break

        if hit_idx is not None:
            used_press[hit_idx] = True
            rts.append(press_times_sorted[hit_idx] - onset)

    # presses innerhalb irgendeines windows, die NICHT als Hit verwendet wurden => extra_in_window
    for i, pt in enumerate(press_times_sorted):
        if used_press[i]:
            continue
        in_any = any(onset <= pt <= end for onset, end in windows_sorted)
        if in_any:
            extra_in_window += 1
            used_press[i] = True  # nicht als false zählen

    false_presses = sum(1 for u in used_press if not u)

    return rts, false_presses, extra_in_window


def read_csv_preserve_comments(path: Path):
    with path.open("r", encoding="utf-8", newline="") as f:
        lines = f.readlines()

    comment_lines = []
    i = 0
    while i < len(lines) and lines[i].startswith("#"):
        comment_lines.append(lines[i])
        i += 1

    if i >= len(lines):
        raise RuntimeError(f"Keine Header-Zeile gefunden in {path.name}")

    header_line = lines[i].rstrip("\n")
    i += 1
    header = next(csv.reader([header_line], delimiter=";"))

    data_lines = [ln.rstrip("\n") for ln in lines[i:] if ln.strip() != ""]
    rows = list(csv.reader(data_lines, delimiter=";"))

    return comment_lines, header, rows


def participant_id_from_filename(name: str) -> str:
    if "_" in name:
        return name.split("_", 1)[0]
    return "unknown"


def process_file(path: Path) -> dict:
    _, header, rows = read_csv_preserve_comments(path)
    header_len = len(header)

    def idx(col):
        return header.index(col) if col in header else None

    ts_i = idx("Timestamp")
    pressed_i = idx("SecTaskPressed")
    is_target_i = idx("SecTaskIsTarget")
    num_i = idx("SecTaskNum")

    if ts_i is None or pressed_i is None:
        raise RuntimeError(f"{path.name}: braucht mindestens Timestamp und SecTaskPressed")

    # Rows auf Header-Länge auffüllen
    norm_rows = []
    for r in rows:
        if len(r) < header_len:
            r = r + [""] * (header_len - len(r))
        norm_rows.append(r)

    # Timestamp extrahieren
    timestamps = [parse_float_comma(r[ts_i]) for r in norm_rows]

    # PRESS EVENTS extrahieren: nur Rising Edge (0 -> 1)
    press_times = []
    prev_pressed = False
    for r, t in zip(norm_rows, timestamps):
        if t is None:
            # Wenn Zeit fehlt, Flanke nicht sinnvoll => prev nicht updaten
            continue

        cur_pressed = is_pressed_value(r[pressed_i])

        # Rising edge: vorher nicht gedrückt, jetzt gedrückt
        if (not prev_pressed) and cur_pressed:
            press_times.append(t)

        prev_pressed = cur_pressed

    # is_target pro Zeile
    if is_target_i is not None:
        is_target = []
        for r in norm_rows:
            v = (r[is_target_i] or "").strip()
            is_target.append(1 if v == "1" else 0)
    else:
        if num_i is None:
            raise RuntimeError(f"{path.name}: weder SecTaskIsTarget noch SecTaskNum vorhanden")
        marked = compute_2back_targets_on_changes(norm_rows, num_i, header_len)
        is_target = [1 if it == "1" else 0 for _, it in marked]

    # Target windows
    windows = extract_target_windows(timestamps, is_target)

    # Treffer/RT/False
    rts, false_presses, extra_in_window = assign_presses_to_targets(press_times, windows)

    targets = len(windows)
    hits = len(rts)
    misses = targets - hits
    accuracy = (hits / targets) if targets > 0 else None
    avg_rt = (sum(rts) / len(rts)) if rts else None

    return {
        "Participant": participant_id_from_filename(path.name),
        "File": path.name,
        "Targets": targets,
        "Hits": hits,
        "Misses": misses,
        "Accuracy": "" if accuracy is None else f"{accuracy:.4f}",
        "AvgRT_sec": "" if avg_rt is None else f"{avg_rt:.4f}",
        "FalsePresses": false_presses,
        "ExtraPressesInWindow": extra_in_window,
        "TotalPressEvents": len(press_times),
        "Window_sec": f"{WINDOW_SECONDS:.2f}",
    }


def main():
    ap = argparse.ArgumentParser(description="Secondary task metrics: Avg RT, accuracy, false presses (all participants).")
    ap.add_argument("input_dir", help="Ordner mit den CSV-Logs")
    ap.add_argument("output_dir", help="Zielordner für Summary")
    args = ap.parse_args()

    in_dir = Path(args.input_dir)
    out_dir = Path(args.output_dir)
    out_dir.mkdir(parents=True, exist_ok=True)

    files = sorted([p for p in in_dir.iterdir() if p.is_file() and p.suffix.lower() == ".csv"])
    if not files:
        print("Keine CSV-Dateien gefunden.")
        return

    results = []
    errors = []

    for f in files:
        try:
            results.append(process_file(f))
        except Exception as e:
            errors.append((f.name, str(e)))

    summary_path = out_dir / "secondary_task_summary.csv"
    fieldnames = [
        "Participant", "File", "Targets", "Hits", "Misses", "Accuracy",
        "AvgRT_sec", "FalsePresses", "ExtraPressesInWindow", "TotalPressEvents", "Window_sec"
    ]

    with summary_path.open("w", encoding="utf-8", newline="") as out:
        w = csv.DictWriter(out, fieldnames=fieldnames, delimiter=";")
        w.writeheader()
        for r in results:
            w.writerow(r)

    print(f"OK: Summary geschrieben -> {summary_path}")

    if errors:
        err_path = out_dir / "secondary_task_errors.txt"
        with err_path.open("w", encoding="utf-8") as out:
            for name, msg in errors:
                out.write(f"{name}: {msg}\n")
        print(f"Achtung: {len(errors)} Dateien hatten Probleme -> {err_path}")


if __name__ == "__main__":
    main()
