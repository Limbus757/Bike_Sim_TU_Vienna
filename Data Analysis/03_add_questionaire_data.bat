@echo off
set "SCRIPT_FOLDER=python scripts"
pushd "%SCRIPT_FOLDER%"
echo Combining Trials and Questionnaire Data...
python combine_trials_questionares.py
popd
pause