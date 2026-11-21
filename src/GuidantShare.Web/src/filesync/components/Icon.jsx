import React from 'react';
// Small stroke icons for icon-only buttons; the button's title/aria-label carries the meaning
export default function Icon({ name }) {
  const common = { width: 15, height: 15, viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor', strokeWidth: 2.2, strokeLinecap: 'round', strokeLinejoin: 'round', 'aria-hidden': true };
  switch (name) {
    case 'play':
      return <svg {...common}><polygon points="6 4 20 12 6 20 6 4" fill="currentColor" /></svg>;
    case 'eye':
      return <svg {...common}><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z" /><circle cx="12" cy="12" r="3" /></svg>;
    case 'pause':
      return <svg {...common}><line x1="8" y1="5" x2="8" y2="19" /><line x1="16" y1="5" x2="16" y2="19" /></svg>;
    case 'resume':
      return <svg {...common}><circle cx="12" cy="12" r="9" /><polygon points="10 8.5 15.5 12 10 15.5 10 8.5" fill="currentColor" /></svg>;
    case 'playAll':
      return <svg {...common}><polygon points="3 5 11 12 3 19 3 5" fill="currentColor" /><polygon points="12 5 20 12 12 19 12 5" fill="currentColor" /></svg>;
    case 'plus':
      return <svg {...common} strokeWidth={2.6}><line x1="12" y1="5" x2="12" y2="19" /><line x1="5" y1="12" x2="19" y2="12" /></svg>;
    case 'trash':
      return <svg {...common}><polyline points="3 6 5 6 21 6" /><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" /></svg>;
    case 'verify':
      return <svg {...common}><circle cx="12" cy="12" r="9" /><polyline points="8 12.5 11 15.5 16.5 9" /></svg>;
    case 'retry':
      return <svg {...common}><polyline points="1 4 1 10 7 10" /><path d="M3.51 15a9 9 0 1 0 2.13-9.36L1 10" /></svg>;
    case 'retryAll':
      return <svg {...common}><polyline points="23 4 23 10 17 10" /><polyline points="1 20 1 14 7 14" /><path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15" /></svg>;
    case 'logs':
      return <svg {...common}><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" /><polyline points="14 2 14 8 20 8" /><line x1="8" y1="13" x2="16" y2="13" /><line x1="8" y1="17" x2="16" y2="17" /></svg>;
    case 'grip':
      return <svg {...common} strokeWidth={3}><line x1="9" y1="6" x2="9.01" y2="6" /><line x1="15" y1="6" x2="15.01" y2="6" /><line x1="9" y1="12" x2="9.01" y2="12" /><line x1="15" y1="12" x2="15.01" y2="12" /><line x1="9" y1="18" x2="9.01" y2="18" /><line x1="15" y1="18" x2="15.01" y2="18" /></svg>;
    case 'stop':
      return <svg {...common}><rect x="6" y="6" width="12" height="12" rx="1.5" fill="currentColor" /></svg>;
    default:
      return null;
  }
}
