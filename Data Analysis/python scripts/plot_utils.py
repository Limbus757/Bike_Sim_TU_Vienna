import pandas as pd
import numpy as np
import matplotlib.pyplot as plt

# Styling Constants
CONDITION_ORDER = ['Baseline', 'HapticsFixed', 'HapticsAdaptive']
COLORS = {'Baseline': '#D3D3D3', 'HapticsFixed': '#c1d4be', 'HapticsAdaptive': '#bec1d4'}

def get_outliers_by_condition(df, metric_col):
    """Calculates outliers for each condition separately."""
    if metric_col not in df.columns: return pd.DataFrame()
    all_outliers = []
    for cond in CONDITION_ORDER:
        cond_data = df[df['Condition'] == cond].copy()
        vals = cond_data[metric_col].dropna()
        if len(vals) < 4: continue 
        q1, q3 = vals.quantile(0.25), vals.quantile(0.75)
        iqr = q3 - q1
        lower, upper = q1 - 1.5 * iqr, q3 + 1.5 * iqr
        outliers = cond_data[(cond_data[metric_col] < lower) | (cond_data[metric_col] > upper)].copy()
        if not outliers.empty:
            outliers['Metric'] = metric_col
            outliers['Value'] = outliers[metric_col]
            outliers['Bound'] = np.where(outliers[metric_col] > upper, 'High', 'Low')
            all_outliers.append(outliers[['participant_id', 'trial_order', 'Condition', 'Metric', 'Value', 'Bound']])
    return pd.concat(all_outliers, ignore_index=True) if all_outliers else pd.DataFrame()

def add_stat_summary_inside(ax, plot_data):
    """Adds M, Med, SD, and CV text beneath the condition names on the left side."""
    x_min, _ = ax.get_xlim()

    for i, vals in enumerate(plot_data):
        if len(vals) == 0: continue
        
        # Calculate stats
        m, med, sd = np.mean(vals), np.median(vals), np.std(vals)
        cv = sd / m if m != 0 else 0
        
        # Format string rounded to 2 decimal places
        stats_str = f"M: {m:.2f} | Med: {med:.2f}\nSD: {sd:.2f} | CV: {cv:.2f}"
        
        # Position: x_min moves it to the left wall. 
        # y = i + 0.65 places it slightly below the center-line (i+1) of the box
        ax.text(x_min, i + 0.65, stats_str, 
                ha='left', va='top', fontsize=7, fontweight='bold',
                color='#333333',
                bbox=dict(facecolor='white', alpha=0.5, edgecolor='none', pad=1))

def create_horizontal_report(df, metric_col, title, save_path):
    fig, (ax1, ax2) = plt.subplots(1, 2, figsize=(20, 8))
    
    for ax, is_zoomed in [(ax1, False), (ax2, True)]:
        plot_data = [df[df['Condition'] == c][metric_col].dropna() for c in CONDITION_ORDER]
        bp = ax.boxplot(plot_data, labels=CONDITION_ORDER, vert=False, patch_artist=True, widths=0.6)
        
        for patch, cond in zip(bp['boxes'], CONDITION_ORDER):
            patch.set_facecolor(COLORS[cond])
            
        for i, vals in enumerate(plot_data):
            y_jitter = np.random.normal(i + 1, 0.04, size=len(vals))
            ax.scatter(vals, y_jitter, alpha=0.4, s=20, color='black', edgecolors='white', zorder=3)
            
        base_avg = df[df['Condition'] == 'Baseline'][metric_col].mean()
        ax.axvline(x=base_avg, color='red', ls='--', lw=1, alpha=0.7)
        
        if is_zoomed:
            all_v = pd.concat(plot_data)
            q1, q3 = all_v.quantile(0.25), all_v.quantile(0.75)
            iqr = q3 - q1
            ax.set_xlim(q1 - 1.5*iqr - (iqr*0.1), q3 + 1.5*iqr + (iqr*0.1))
            ax.set_title(f"{title} (Zoomed View)", fontsize=14, fontweight='bold')
        else:
            ax.set_title(f"{title} (Full View)", fontsize=14, fontweight='bold')
            
        # Call updated function
        add_stat_summary_inside(ax, plot_data)
            
        ax.grid(axis='x', linestyle=':', alpha=0.6)
        ax.spines['top'].set_visible(False)
        ax.spines['right'].set_visible(False)

    plt.tight_layout(pad=4.0)
    plt.savefig(save_path, dpi=200)
    plt.close()
    return get_outliers_by_condition(df, metric_col)