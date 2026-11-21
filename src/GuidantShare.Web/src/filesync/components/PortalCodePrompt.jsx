import React, { useState, useEffect, useRef } from 'react';
import { fetchPendingPortalCode, submitPortalCode, cancelPortalCode } from '../fsApi';

// Shown above everything when a portal sign-in (SCAN) is waiting for the one-time code it emailed.
// Appears for Test Connection, manual runs, and scheduled runs alike.
export default function PortalCodePrompt() {
  const [pending, setPending] = useState(null);
  const [code, setCode] = useState('');
  const [sending, setSending] = useState(false);
  const [message, setMessage] = useState(null);
  const [now, setNow] = useState(Date.now());
  const requestKey = useRef(null);

  useEffect(() => {
    const poll = async () => {
      try {
        const data = await fetchPendingPortalCode();
        const next = data.pending || null;
        const key = next ? next.requestedUtc : null;
        if (key !== requestKey.current) {
          // A new request (or none): start with an empty box
          requestKey.current = key;
          setCode('');
          setMessage(null);
          setSending(false);
        }
        setPending(next);
      } catch {
        // Backend unreachable; the rest of the dashboard already shows that
      }
    };
    poll();
    const interval = setInterval(poll, 3000);
    const clock = setInterval(() => setNow(Date.now()), 1000);
    return () => {
      clearInterval(interval);
      clearInterval(clock);
    };
  }, []);

  if (!pending) return null;

  const secondsLeft = Math.max(0, Math.floor((new Date(pending.expiresUtc).getTime() - now) / 1000));
  const timeLeft = `${Math.floor(secondsLeft / 60)}:${String(secondsLeft % 60).padStart(2, '0')}`;

  const handleSubmit = async (e) => {
    e.preventDefault();
    if (!code.trim()) return;
    setSending(true);
    setMessage(null);
    try {
      await submitPortalCode(code.trim());
      setMessage({ ok: true, text: `Code sent. ${pending.portal} is signing in...` });
    } catch (err) {
      setSending(false);
      setMessage({ ok: false, text: err.message });
    }
  };

  const handleCancel = async () => {
    try {
      await cancelPortalCode();
      setPending(null);
    } catch (err) {
      setMessage({ ok: false, text: err.message });
    }
  };

  return (
    <div className="modal-overlay" id="portal-code-overlay" style={{ zIndex: 10000 }}>
      <div className="modal-content" id="portal-code-content" style={{ maxWidth: '440px', width: '92%' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '10px' }}>
          <span className="status-badge badge-blue">{pending.portal} SIGN-IN</span>
          <h3 style={{ fontSize: '1.15rem', fontWeight: 700, color: 'var(--text-main)', margin: 0 }}>Enter one-time code</h3>
        </div>
        <div style={{ fontSize: '0.85rem', color: 'var(--text-secondary)', marginBottom: '6px' }}>{pending.prompt}</div>
        <div style={{ fontSize: '0.78rem', color: 'var(--text-muted)', marginBottom: '14px' }}>
          Type the code from the email. The sign-in waits {timeLeft} more.
        </div>

        <form onSubmit={handleSubmit}>
          <input
            type="text"
            id="input-portal-code"
            value={code}
            onChange={(e) => setCode(e.target.value)}
            autoFocus
            autoComplete="one-time-code"
            inputMode="numeric"
            placeholder="Security code"
            disabled={sending}
            style={{
              width: '100%',
              padding: '10px 12px',
              borderRadius: '8px',
              border: '1px solid var(--card-border)',
              background: 'var(--gray-bg)',
              color: 'var(--text-main)',
              fontFamily: 'var(--font-mono)',
              fontSize: '1.1rem',
              letterSpacing: '0.15em',
              marginBottom: '12px'
            }}
          />
          {message && (
            <div style={{
              fontSize: '0.82rem',
              marginBottom: '12px',
              color: message.ok ? 'var(--green-text)' : 'var(--red-text)'
            }}>
              {message.text}
            </div>
          )}
          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px' }}>
            <button type="button" id="btn-cancel-portal-code" className="btn" onClick={handleCancel}>
              Cancel
            </button>
            <button type="submit" id="btn-submit-portal-code" className="btn btn-primary" disabled={sending || !code.trim()}>
              {sending ? 'Signing in...' : 'Submit Code'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
