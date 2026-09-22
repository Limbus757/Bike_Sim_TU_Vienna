@echo off
title Driving Simulator Data Pipeline

:: CHANGE 'scripts' TO THE NAME OF YOUR SUBFOLDER
set SCRIPT_FOLDER=python scripts

echo Entering folder: %SCRIPT_FOLDER%
pushd %SCRIPT_FOLDER%

echo Step 1: Data Cleaning and Grouping...
python data_cleaning_and_grouping.py
if %errorlevel% neq 0 (echo Error in Step 1! & popd & pause & exit /b)

echo Step 2: Extracting Trial Metrics...
python extract_logged_trial_metrics.py
if %errorlevel% neq 0 (echo Error in Step 2! & popd & pause & exit /b)

echo Step 3: Combining Trials and Questionnaire Data...
python combine_trials_questionares.py
if %errorlevel% neq 0 (echo Error in Step 3! & popd & pause & exit /b)

echo --------------------------------------------------
echo ALL STEPS COMPLETED SUCCESSFULLY!

:: Return to the original parent folder
popd
pause