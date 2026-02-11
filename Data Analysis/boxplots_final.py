import pandas as pd
import matplotlib.pyplot as plt
import numpy as np
import os
from matplotlib.lines import Line2D
from matplotlib.patches import Patch

# ============================================================================
# CONFIGURATION CONSTANTS
# ============================================================================
CONDITION_ORDER = ['Baseline', 'HapticsFixed', 'HapticsAdaptive']
CONDITION_COLORS = {
    'Baseline': '#D3D3D3',
    'HapticsFixed': '#c1d4be',
    'HapticsAdaptive': '#bec1d4'
}
BASELINE_REF_COLOR = 'red'
RESPONSE_WINDOW_SECONDS = 1.5
DECIMAL_PLACES = 4

# Direct paths to your cleaned condition files
DATA_FILENAMES = {
    'Baseline': 'Baseline.csv',
    'HapticsFixed': 'HapticsFixed.csv', 
    'HapticsAdaptive': 'HapticsAdaptive.csv'
}
DATA_SEARCH_PATHS = ["cleaned_grouped_output/grouped_by_condition"]

# ============================================================================
# OPTIMIZED DATA PROCESSING FUNCTIONS
# ============================================================================

def calculate_secondary_task_performance_fast(trial_data):
    """
    OPTIMIZED: Calculate cognitive performance metrics for secondary task.
    Processes at trial level instead of row level.
    """
    # Sort by timestamp
    trial_data = trial_data.sort_values('Timestamp').reset_index(drop=True)
    
    # Get arrays for faster access
    timestamps = trial_data['Timestamp'].values
    target_flags = trial_data['SecTaskIsTarget'].values
    press_flags = trial_data['SecTaskPressed'].values
    
    # Find stimulus changes (target or distractor)
    stimulus_changes = []
    for i in range(len(trial_data)):
        if target_flags[i] != -1:  # Valid stimulus
            if i == 0 or target_flags[i] != target_flags[i-1]:
                stimulus_changes.append((timestamps[i], target_flags[i]))
    
    if not stimulus_changes:
        return pd.Series({
            'HitRate': 0,
            'FARate': 0,
            'CorrectedAcc': 0
        })
    
    # Separate targets and distractors
    target_times = [t for t, flag in stimulus_changes if flag == 1]
    distractor_count = sum(1 for _, flag in stimulus_changes if flag == 0)
    
    # Find button press events (transitions from 0 to 1)
    press_times = []
    for i in range(1, len(trial_data)):
        if press_flags[i] == 1 and press_flags[i-1] == 0:
            press_times.append(timestamps[i])
    
    # Count hits
    hits = 0
    used_presses = set()
    
    for target_time in target_times:
        window_end = target_time + RESPONSE_WINDOW_SECONDS
        for press_idx, press_time in enumerate(press_times):
            if press_idx not in used_presses and target_time <= press_time <= window_end:
                used_presses.add(press_idx)
                hits += 1
                break
    
    # Calculate rates
    hit_rate = (hits / len(target_times)) * 100 if target_times else 0
    false_alarms = len(press_times) - len(used_presses)
    false_alarm_rate = (false_alarms / distractor_count) * 100 if distractor_count > 0 else 0
    corrected_accuracy = hit_rate - false_alarm_rate
    
    return pd.Series({
        'HitRate': hit_rate,
        'FARate': false_alarm_rate,
        'CorrectedAcc': corrected_accuracy
    })


def load_and_process_condition_data_fast(condition_name, filename):
    """
    OPTIMIZED: Load data and compute summary metrics.
    """
    # Find the data file
    file_path = None
    for search_path in DATA_SEARCH_PATHS:
        full_path = os.path.join(search_path, filename)
        if os.path.exists(full_path):
            file_path = full_path
            break
    
    if not file_path:
        print(f"Warning: Could not find {filename}")
        return None
    
    print(f"  Loading {filename}...")
    
    try:
        # Load data with optimized settings
        raw_data = pd.read_csv(
            file_path, 
            sep=';', 
            decimal=',',
            usecols=[  # Only load columns we actually need
                'participant_id', 'trial_order', 'Timestamp', 
                'Speed_KmH', 'CTE_Meters', 'B_HeadingError_Deg', 
                'LkaNormEffort', 'LKA_Switch', 'LKA_Engaged',
                'SecTaskNum', 'SecTaskIsTarget', 'SecTaskPressed'
            ]
        )
    except Exception as e:
        # Fallback to loading all columns
        print(f"    Note: Loading all columns due to: {e}")
        raw_data = pd.read_csv(file_path, sep=';', decimal=',')
    
    print(f"    Loaded {len(raw_data):,} rows")
    
    # Group by trial first to minimize calculations
    grouped = raw_data.groupby(['participant_id', 'trial_order'])
    
    # Pre-allocate lists for results
    results = []
    
    print(f"    Processing {len(grouped)} trials...")
    
    for (participant_id, trial_order), trial_group in grouped:
        # Calculate absolute values for this trial
        abs_cte = trial_group['CTE_Meters'].abs()
        abs_heading = trial_group['B_HeadingError_Deg'].abs()
        abs_effort = trial_group['LkaNormEffort'].abs()
        
        # Driving metrics
        driving_metrics = {
            'participant_id': participant_id,
            'trial_order': trial_order,
            'Avg_Speed': trial_group['Speed_KmH'].mean(),
            'Laptime': trial_group['Timestamp'].max() - trial_group['Timestamp'].min(),
            'SDLP': trial_group['CTE_Meters'].std(),
            'Mean_CTE': abs_cte.mean(),
            'Heading_Variability': trial_group['B_HeadingError_Deg'].std(),
            'Mean_HeadingErr': abs_heading.mean(),
            'LkaEffort': abs_effort.mean(),
            'LKA_Switch_Pct': trial_group['LKA_Switch'].mean() * 100,
            'LKA_Engaged_Pct': trial_group['LKA_Engaged'].mean() * 100
        }
        
        # Secondary task metrics
        sec_task_metrics = calculate_secondary_task_performance_fast(trial_group)
        
        # Combine
        trial_result = {**driving_metrics, **sec_task_metrics.to_dict()}
        results.append(trial_result)
    
    # Convert to DataFrame
    combined_metrics = pd.DataFrame(results)
    combined_metrics['Condition'] = condition_name
    
    print(f"    Processed {len(results)} trials")
    return combined_metrics


def create_visualization_layout():
    """Define the layout of metrics in the visualization grid."""
    return {
        'row1': [
            ('Mean_CTE', 'Lane Error (m)', 'Lane_Error'),
            ('SDLP', 'SD of Lane Position', 'SD_Lane_Position'),
            ('Mean_HeadingErr', 'Heading Error (°)', 'Heading_Error'),
            ('Heading_Variability', 'Heading Variability (°)', 'Heading_Variability'),
            ('LkaEffort', 'LKA Effort', 'LKA_Effort'),
            ('LKA_Switch_Pct', 'System Usage (%)', 'System_Usage')
        ],
        'row2': [
            ('LKA_Engaged_Pct', 'Torque Engagement (%)', 'Torque_Engagement'),
            ('HitRate', 'ST: Hit Rate (%)', 'ST_Hit_Rate'),
            ('FARate', 'ST: False Alarm Rate (%)', 'ST_False_Alarm_Rate'),
            ('CorrectedAcc', 'ST: Corrected Accuracy (%)', 'ST_Corrected_Accuracy'),
            ('Laptime', 'Lap Time (s)', 'Lap_Time'),
            ('Avg_Speed', 'Average Speed (km/h)', 'Average_Speed')
        ]
    }


def create_legend_elements():
    """Create legend elements for the visualization."""
    return [
        Line2D([0], [0], color='black', lw=1.5, label='Median'),
        Line2D([0], [0], color='black', lw=1.2, ls='--', label='Mean'),
        Line2D([0], [0], color=BASELINE_REF_COLOR, lw=1.2, ls='--', label='Baseline Reference'),
        Patch(facecolor='#D3D3D3', label='Baseline'),
        Patch(facecolor='#c1d4be', label='Fixed Haptics'),
        Patch(facecolor='#bec1d4', label='Adaptive Haptics')
    ]


def add_statistical_annotations(axis, data_values, condition_index, decimal_places=DECIMAL_PLACES):
    """Add statistical annotations to plot."""
    if len(data_values) == 0:
        return {'condition': CONDITION_ORDER[condition_index], 'mean': 0, 'std': 0, 'cv': 0}
    
    mean_value = np.mean(data_values)
    std_value = np.std(data_values)
    coefficient_variation = (std_value / mean_value) if mean_value != 0 else 0
    
    annotation_text = f'M: {mean_value:.{decimal_places}f}\nSD: {std_value:.{decimal_places}f}\nCV: {coefficient_variation:.{decimal_places}f}'
    
    axis.text(
        condition_index + 1, -0.06,
        annotation_text,
        transform=axis.get_xaxis_transform(),
        ha='center', va='top', fontsize=10, fontweight='bold',
        bbox=dict(facecolor='white', alpha=0.7, edgecolor='none', pad=1),
        zorder=4
    )
    
    axis.hlines(
        mean_value,
        condition_index + 0.75,
        condition_index + 1.25,
        colors='black',
        linestyles='--',
        linewidth=1.2,
        zorder=5
    )
    
    return {
        'condition': CONDITION_ORDER[condition_index],
        'mean': mean_value,
        'std': std_value,
        'cv': coefficient_variation
    }


def create_condition_boxplot(axis, metric_data, metric_name, plot_title):
    """Create a boxplot for a specific metric."""
    # Filter data by condition
    plot_data = []
    for condition in CONDITION_ORDER:
        condition_data = metric_data[metric_data['Condition'] == condition][metric_name].dropna()
        plot_data.append(condition_data)
    
    # Create boxplot
    boxplot = axis.boxplot(
        plot_data,
        tick_labels=CONDITION_ORDER,
        patch_artist=True,
        widths=0.6,
        medianprops=dict(color='black', linewidth=1.5),
        zorder=2
    )
    
    # Color boxes by condition
    for box_patch, condition in zip(boxplot['boxes'], CONDITION_ORDER):
        box_patch.set_facecolor(CONDITION_COLORS[condition])
    
    # Add individual data points with jitter
    statistics_by_condition = []
    for condition_index, values in enumerate(plot_data):
        if len(values) > 0:
            # Add jittered points
            x_jitter = np.random.normal(condition_index + 1, 0.06, size=len(values))
            x_jitter = np.clip(x_jitter, condition_index + 0.85, condition_index + 1.15)
            axis.scatter(x_jitter, values, alpha=0.5, s=30, color='black', edgecolors='white', zorder=3)
            
            # Add statistics
            stats = add_statistical_annotations(axis, values, condition_index)
            stats['metric'] = metric_name
            statistics_by_condition.append(stats)
    
    # Add baseline reference line
    if 'Baseline' in metric_data['Condition'].unique():
        baseline_data = metric_data[metric_data['Condition'] == 'Baseline'][metric_name].dropna()
        if len(baseline_data) > 0:
            axis.axhline(
                y=baseline_data.mean(),
                color=BASELINE_REF_COLOR,
                linestyle='--',
                linewidth=1.5,
                alpha=0.8,
                zorder=1
            )
    
    # Set axis limits
    all_values = metric_data[metric_name].dropna()
    if len(all_values) > 0:
        val_min, val_max = all_values.min(), all_values.max()
        val_range = val_max - val_min
        padding = val_range * 0.10 if val_range != 0 else 10
        axis.set_ylim(val_min - padding, val_max + padding)
    
    axis.set_title(plot_title, fontweight='bold', fontsize=18, pad=10)
    axis.grid(axis='y', linestyle=':', alpha=0.5)
    
    return statistics_by_condition


def export_individual_plots(figure, axes, output_directory="individual_plots_svg"):
    """Export each subplot as a separate SVG file."""
    if not os.path.exists(output_directory):
        os.makedirs(output_directory)
    
    metric_layout = create_visualization_layout()
    
    for row_idx, row_key in enumerate(['row1', 'row2']):
        for col_idx, (metric_name, plot_title, filename_base) in enumerate(metric_layout[row_key]):
            if metric_name == 'None':
                continue
            
            axis = axes[row_idx, col_idx]
            figure.savefig(
                f"{output_directory}/{filename_base}.svg",
                bbox_inches='tight',
                format='svg',
                transparent=True
            )


def save_full_precision_statistics(all_statistics, output_file="full_precision_statistics.csv"):
    """Save statistics to CSV."""
    if all_statistics:
        pd.DataFrame(all_statistics).to_csv(output_file, index=False)
        return True
    return False


# ============================================================================
# MAIN EXECUTION (OPTIMIZED)
# ============================================================================

def main():
    """Optimized main execution pipeline."""
    import time
    start_time = time.time()
    
    print("=" * 80)
    print("OPTIMIZED HAPTIC INTERVENTION ANALYSIS")
    print("=" * 80)
    
    # Step 1: Load and process data
    print("\nSTEP 1: Loading and processing data...")
    all_condition_data = []
    
    for condition_name, filename in DATA_FILENAMES.items():
        print(f"\nProcessing {condition_name}:")
        condition_start = time.time()
        
        condition_metrics = load_and_process_condition_data_fast(condition_name, filename)
        
        if condition_metrics is not None:
            all_condition_data.append(condition_metrics)
            condition_time = time.time() - condition_start
            print(f"  ✓ Done in {condition_time:.1f}s: {len(condition_metrics)} trials")
    
    if not all_condition_data:
        print("Error: No data loaded.")
        return
    
    # Combine data
    combined_data = pd.concat(all_condition_data, ignore_index=True)
    combined_data['Condition'] = pd.Categorical(
        combined_data['Condition'],
        categories=CONDITION_ORDER,
        ordered=True
    )
    
    load_time = time.time() - start_time
    print(f"\n✓ Data loaded in {load_time:.1f}s")
    print(f"  Total trials: {len(combined_data)}")
    print(f"  Participants: {combined_data['participant_id'].nunique()}")
    
    # Step 2: Create visualization
    print("\nSTEP 2: Creating visualization...")
    viz_start = time.time()
    
    figure, axes_grid = plt.subplots(2, 6, figsize=(38, 25))
    
    # Create plots
    metric_layout = create_visualization_layout()
    all_collected_statistics = []
    
    for row_idx, row_key in enumerate(['row1', 'row2']):
        for col_idx, (metric_name, plot_title, filename_base) in enumerate(metric_layout[row_key]):
            axis = axes_grid[row_idx, col_idx]
            
            if metric_name == 'None':
                axis.axis('off')
                continue
            
            plot_stats = create_condition_boxplot(axis, combined_data, metric_name, plot_title)
            if plot_stats:
                all_collected_statistics.extend(plot_stats)
    
    # Add legend and title
    figure.legend(
        handles=create_legend_elements(),
        loc='upper left',
        bbox_to_anchor=(0.01, 0.98),
        ncol=1,
        fontsize=14,
        frameon=True,
        framealpha=0.9,
        edgecolor='black'
    )
    
    plt.suptitle(
        f'Haptic Intervention: Usage, Trust & Performance Analysis',
        fontsize=32,
        fontweight='bold',
        y=0.98
    )
    
    plt.subplots_adjust(
        left=0.10, bottom=0.12, right=0.95, top=0.92,
        hspace=0.5, wspace=0.22
    )
    
    viz_time = time.time() - viz_start
    print(f"✓ Visualization created in {viz_time:.1f}s")
    
    # Step 3: Save outputs
    print("\nSTEP 3: Saving outputs...")
    save_start = time.time()
    
    figure.savefig('haptics_main_report.png', dpi=300, bbox_inches='tight')
    export_individual_plots(figure, axes_grid)
    
    if all_collected_statistics:
        save_full_precision_statistics(all_collected_statistics)
        print(f"  Statistics saved: {len(all_collected_statistics)} records")
    
    save_time = time.time() - save_start
    total_time = time.time() - start_time
    
    print(f"\n✓ Outputs saved in {save_time:.1f}s")
    print(f"\n" + "=" * 80)
    print(f"ANALYSIS COMPLETE - Total time: {total_time:.1f}s")
    print("=" * 80)
    print("Outputs:")
    print("  - haptics_main_report.png")
    print("  - individual_plots_svg/")
    print("  - full_precision_statistics.csv")
    print("=" * 80)
    
    plt.show()


if __name__ == "__main__":
    main()