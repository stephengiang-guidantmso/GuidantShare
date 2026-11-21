import React from 'react';

// control: optional element (e.g. a time-range selector) shown at the top right, in place of the icon
export default function StatCard({ id, label, value, subtitle, badgeText, badgeType = 'nominal', icon, control }) {
  return (
    <div className="telemetry-card" id={id}>
      <div className="card-top-row">
        <span className="card-metric-label">{label}</span>
        {control || (icon && <span style={{ color: 'var(--primary-blue)', opacity: 0.85 }}>{icon}</span>)}
      </div>

      <div className="card-metric-value">
        {value}
      </div>

      <div className="card-bottom-row">
        <span>{subtitle}</span>
        {badgeText && (
          <span className={`status-badge badge-${badgeType}`}>
            {badgeText}
          </span>
        )}
      </div>
    </div>
  );
}
