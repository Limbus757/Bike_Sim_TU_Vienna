@echo off
set "SCRIPT_FOLDER=python scripts"
pushd "%SCRIPT_FOLDER%"
echo Running Data Cleaning and Grouping...
python data_cleaning_and_grouping.py
popd
pause