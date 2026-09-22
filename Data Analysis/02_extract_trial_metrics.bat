@echo off
set "SCRIPT_FOLDER=python scripts"
pushd "%SCRIPT_FOLDER%"
echo Running Metric Extraction...
python extract_logged_trial_metrics.py
popd
pause