// Keyed by canvasId (not a single module-level instance) — Home.razor and
// WorkspaceDetail.razor import this same module and can both be alive at once under
// Blazor's enhanced navigation (no full page reload between them), so a shared
// singleton instance let one page's dispose destroy the other page's chart.
const chartInstances = new Map();

export function render(canvasId, labels, values) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) {
        return;
    }

    const styles = getComputedStyle(document.documentElement);
    const primary = styles.getPropertyValue('--color-primary').trim() || '#2563EB';
    const muted = styles.getPropertyValue('--color-text-muted').trim() || '#6B7280';
    const grid = styles.getPropertyValue('--color-divider').trim() || '#EEF2F7';

    destroy(canvasId);

    const chartInstance = new Chart(canvas, {
        type: 'line',
        data: {
            labels: labels,
            datasets: [{
                label: 'Concluídas',
                data: values,
                borderColor: primary,
                backgroundColor: primary + '26',
                fill: true,
                tension: 0.35,
                pointRadius: 3,
                pointBackgroundColor: primary
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: { display: false },
                tooltip: { intersect: false, mode: 'index' }
            },
            scales: {
                x: { ticks: { color: muted }, grid: { display: false } },
                y: { beginAtZero: true, ticks: { color: muted, stepSize: 1, precision: 0 }, grid: { color: grid } }
            }
        }
    });

    chartInstances.set(canvasId, chartInstance);
}

export function destroy(canvasId) {
    const chartInstance = chartInstances.get(canvasId);
    if (chartInstance) {
        chartInstance.destroy();
        chartInstances.delete(canvasId);
    }
}
