export function Logo({ size = 26, dots = '#4ADE80' }: { size?: number; dots?: string }) {
  return (
    <svg width={size} height={size} viewBox="0 0 26 26" fill="none" aria-hidden="true">
      <rect x="2" y="3" width="22" height="8" rx="2.5" stroke="#8EA2FF" strokeWidth="2" />
      <rect x="2" y="15" width="22" height="8" rx="2.5" stroke="#8EA2FF" strokeWidth="2" />
      <circle cx="7" cy="7" r="1.6" fill={dots} />
      <circle cx="7" cy="19" r="1.6" fill={dots} />
    </svg>
  );
}
