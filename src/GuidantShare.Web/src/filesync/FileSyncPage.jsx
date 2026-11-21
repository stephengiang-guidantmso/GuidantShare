import React, { useState, useEffect, useRef } from 'react';
import StatCard from './components/StatCard';
import WorkflowList from './components/WorkflowList';
import TransferLedger from './components/TransferLedger';
import RetryQueue from './components/RetryQueue';
import SftpExplorer from './components/SftpExplorer';
import VerifySync from './components/VerifySync';
import LogViewer from './components/LogViewer';
import Modal from './components/Modal';
import ObservationModal from './components/ObservationModal';
import JobEditorModal from './components/JobEditorModal';
import './filesync.css';
import PortalCodePrompt from './components/PortalCodePrompt';
import { 
  fetchHealth, 
  fetchWorkflows, 
  triggerWorkflowRun, 
  toggleWorkflow, 
  createWorkflow,
  updateWorkflow,
  deleteWorkflow,
  fetchTransfers, 
  fetchTransferCount,
  cancelWorkflowRun,
  retryTransfer, 
  retryAllTransfers,
  clearTransfers,
  fetchLogs,
  fetchCatalog
} from './fsApi';

export default function FileSyncPage() {
  const [health, setHealth] = useState(null);
  const [workflows, setWorkflows] = useState([]);
  const [transfers, setTransfers] = useState([]);
  const [logs, setLogs] = useState([]);
  const [transferCount, setTransferCount] = useState({ totalTransferred: 0 });
  // Time range for the Total Transferred card: hours (1, 12, 24) or '' for all time; remembered in this browser
  const [transferWindow, setTransferWindow] = useState(() => {
    try { return localStorage.getItem('transferWindow') ?? ''; } catch { return ''; }
  });
  const transferWindowRef = useRef(transferWindow);
  const [activeTab, setActiveTab] = useState('workflows');
  // Job to pre-select in the Transfer Ledger when opened from a job's "files transferred" link
  const [ledgerJobFilter, setLedgerJobFilter] = useState('');
  const changeTab = (tab) => {
    setLedgerJobFilter('');
    setActiveTab(tab);
  };
  const openLedgerForJob = (workflowId) => {
    setLedgerJobFilter(workflowId);
    setActiveTab('ledger');
  };
  const [selectedTransfer, setSelectedTransfer] = useState(null);
  const [observeWorkflow, setObserveWorkflow] = useState(null);
  const [editingJob, setEditingJob] = useState(null);
  const [isCreatingJob, setIsCreatingJob] = useState(false);
  // Light or dark, only for this page; remembered in this browser
  const [theme, setTheme] = useState(() => {
    try { return localStorage.getItem('fs-theme') || 'light'; } catch { return 'light'; }
  });
  const toggleTheme = () => {
    setTheme(prev => {
      const next = prev === 'dark' ? 'light' : 'dark';
      try { localStorage.setItem('fs-theme', next); } catch { /* private window: not remembered */ }
      return next;
    });
  };
  // Jobs whose Plan / Folder name leave them off the website, or whose ShareFile folders disagree
  const [catalogIssues, setCatalogIssues] = useState([]);

  const loadData = async () => {
    try {
      const [hData, wData, tData, lData, cData, catalog] = await Promise.all([
        fetchHealth().catch(() => null),
        fetchWorkflows().catch(() => []),
        fetchTransfers().catch(() => []),
        fetchLogs().catch(() => []),
        fetchTransferCount(transferWindowRef.current || null).catch(() => null),
        fetchCatalog().catch(() => null)
      ]);
      if (catalog) setCatalogIssues(catalog.issues || []);
      setHealth(hData);
      if (cData) setTransferCount(cData);
      setWorkflows(wData);
      setTransfers(tData);
      setLogs(lData);
    } catch (err) {
      console.error('Failed to poll FileSync backend telemetry:', err);
    }
  };

  useEffect(() => {
    loadData();
    const interval = setInterval(loadData, 8000);
    return () => clearInterval(interval);
  }, []);

  const handleTriggerRun = async (wfId, mode) => {
    const result = await triggerWorkflowRun(wfId, mode);
    await loadData();
    return result;
  };

  const handleCancelRun = async (wfId) => {
    await cancelWorkflowRun(wfId).catch(() => null);
    await loadData();
  };

  const handleToggleWorkflow = async (wfId) => {
    await toggleWorkflow(wfId);
    await loadData();
  };

  const handleRetryTransfer = async (transferId) => {
    await retryTransfer(transferId);
    await loadData();
  };

  const handleRetryAll = async () => {
    const result = await retryAllTransfers();
    await loadData();
    return result;
  };

  const handleClearTransfers = async () => {
    await clearTransfers('ALL');
    await loadData();
  };

  const handleClearRetries = async () => {
    await clearTransfers('retries');
    await loadData();
  };

  // Starts every enabled job at once; resolves when the last one finishes (a manual run returns when it completes)
  const handleRunAllDue = async () => {
    const runs = workflows
      .filter(w => w.isEnabled)
      .map(wf => triggerWorkflowRun(wf.workflowId, 'Live').catch(err => console.error(`Run failed for ${wf.workflowId}:`, err)));
    setTimeout(loadData, 1000); // show each job's running spinner right away
    await Promise.all(runs);
    await loadData();
  };

  const handleSaveJob = async (jobData, existingId) => {
    if (existingId) {
      await updateWorkflow(existingId, jobData);
    } else {
      await createWorkflow(jobData);
    }
    await loadData();
  };

  const handleDeleteJob = async (workflowId) => {
    await deleteWorkflow(workflowId);
    await loadData();
  };

  // Accurate COUNT(*) from the backend; the polled transfers list is capped and must not be counted
  const totalTransferredFiles = transferCount?.totalTransferred ?? 0;
  const TRANSFER_WINDOWS = [['1', 'Last hour'], ['12', 'Last 12 hours'], ['24', 'Last 24 hours'], ['', 'All time']];
  const handleTransferWindowChange = (e) => {
    const value = e.target.value;
    setTransferWindow(value);
    transferWindowRef.current = value;
    try { localStorage.setItem('transferWindow', value); } catch { /* private window: not remembered */ }
    fetchTransferCount(value || null).then(setTransferCount).catch(() => {});
  };
  const retryCount = (transfers || []).filter(t => t.status === 'WaitingRetry' || t.status === 'Failed' || t.status === 'Quarantined').length;
  const activeCount = (workflows || []).filter(w => w.isEnabled).length;

  return (
    <div className={`fs-root ${theme === 'dark' ? 'dark' : ''}`} data-theme={theme}>
      <div className="container" id="app-root">
      <nav className="top-nav" id="top-navbar" style={{ justifyContent: 'space-between' }}>
        <div className="nav-actions" id="nav-actions">
          <button className={`nav-btn ${activeTab === 'workflows' ? 'active' : ''}`} onClick={() => changeTab('workflows')}>Transfer Jobs</button>
          <button className={`nav-btn ${activeTab === 'ledger' ? 'active' : ''}`} onClick={() => changeTab('ledger')}>Transfer Ledger</button>
          <button className={`nav-btn ${activeTab === 'retries' ? 'active' : ''}`} onClick={() => changeTab('retries')}>Retry Queue</button>
          <button className={`nav-btn ${activeTab === 'explorer' ? 'active' : ''}`} onClick={() => changeTab('explorer')}>SFTP Explorer</button>
          <button className={`nav-btn ${activeTab === 'logs' ? 'active' : ''}`} onClick={() => changeTab('logs')}>Audit Logs</button>
          <button className={`nav-btn ${activeTab === 'verify' ? 'active' : ''}`} onClick={() => changeTab('verify')}>Verify Sync</button>
        </div>
        <div className="nav-actions">
          <span className="audit-time">{health?.timestampUtc ? `FileSync reachable · ${new Date(health.timestampUtc).toLocaleTimeString()}` : 'FileSync not reachable'}</span>
          <button className="nav-btn" onClick={() => loadData()} title="Reload now">Refresh</button>
          <button className="nav-btn" onClick={toggleTheme} aria-label="Toggle theme" title="Toggle light/dark">
            <img src="/day-and-night.svg" alt="" className="theme-icon" />
          </button>
        </div>
      </nav>

      {catalogIssues.length > 0 && (
        <div className="fs-issues" role="alert" style={{ margin: '0 0 16px', padding: '12px 16px', borderRadius: '10px', background: 'var(--yellow-bg)', color: 'var(--yellow-text)', border: '1px solid var(--yellow-border)', fontSize: '0.85rem' }}>
          <strong>Website catalog needs attention:</strong>
          <ul style={{ margin: '6px 0 0 18px' }}>
            {catalogIssues.map((i, idx) => <li key={idx}>{i.message}</li>)}
          </ul>
        </div>
      )}

      {/* 3-Card Hardware / Telemetry Grid */}
      <div className="hw-grid" id="telemetry-hardware-grid" style={{ gridTemplateColumns: 'repeat(3, 1fr)' }}>
        <StatCard
          id="card-active-workflows"
          label="ACTIVE TRANSFER JOBS"
          value={`${activeCount} / ${workflows.length}`}
          subtitle="Continuous Polling Engine"
          badgeText={activeCount > 0 ? "NOMINAL" : "OFFLINE"}
          badgeType={activeCount > 0 ? "nominal" : "offline"}
          icon={
            <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <rect x="2" y="2" width="20" height="8" rx="2" ry="2"></rect>
              <rect x="2" y="14" width="20" height="8" rx="2" ry="2"></rect>
              <line x1="6" y1="6" x2="6.01" y2="6"></line>
              <line x1="6" y1="18" x2="6.01" y2="18"></line>
            </svg>
          }
        />

        <StatCard
          id="card-total-transferred-files"
          label="TOTAL TRANSFERRED FILES"
          value={totalTransferredFiles}
          subtitle={transferWindow ? `${TRANSFER_WINDOWS.find(([v]) => v === transferWindow)?.[1] || ''}, verified` : 'All time, verified'}
          control={
            <select
              id="sel-transfer-window"
              value={transferWindow}
              onChange={handleTransferWindowChange}
              aria-label="Time range for total transferred files"
              style={{ fontSize: '0.75rem', padding: '3px 6px', borderRadius: '6px', border: '1px solid var(--card-border)', background: 'var(--gray-bg)', color: 'var(--text-main)', cursor: 'pointer' }}
            >
              {TRANSFER_WINDOWS.map(([v, label]) => <option key={label} value={v}>{label}</option>)}
            </select>
          }
          badgeText="HEALTHY"
          badgeType="healthy"
          icon={
            <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"></path>
              <polyline points="22 4 12 14.01 9 11.01"></polyline>
            </svg>
          }
        />

        <StatCard
          id="card-active-retries"
          label="ACTIVE RETRIES"
          value={retryCount}
          subtitle="Exponential Backoff"
          badgeText={retryCount > 0 ? `${retryCount} PENDING` : "NOMINAL"}
          badgeType={retryCount > 0 ? "warning" : "nominal"}
          icon={
            <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <circle cx="12" cy="12" r="10"></circle>
              <line x1="12" y1="8" x2="12" y2="12"></line>
              <line x1="12" y1="16" x2="12.01" y2="16"></line>
            </svg>
          }
        />
      </div>

      {/* Main Tab Panels */}
      {activeTab === 'workflows' && (
        <WorkflowList
          workflows={workflows}
          onShowTransfers={openLedgerForJob}
          onTriggerRun={handleTriggerRun}
          onToggleWorkflow={handleToggleWorkflow}
          onRefresh={loadData}
          onOpenObserve={setObserveWorkflow}
          onEditJob={(wf) => setEditingJob(wf)}
          onOpenNewJob={() => setIsCreatingJob(true)}
          onRunAll={handleRunAllDue}
          onCancelRun={handleCancelRun}
        />
      )}

      {activeTab === 'ledger' && (
        <TransferLedger
          key={ledgerJobFilter || 'all'}
          initialJobFilter={ledgerJobFilter}
          transfers={transfers}
          workflows={workflows}
          onSelectTransfer={setSelectedTransfer}
          onClearTransfers={handleClearTransfers}
          onRefresh={loadData}
        />
      )}

      {activeTab === 'retries' && (
        <RetryQueue
          transfers={transfers}
          workflows={workflows}
          onRetryTransfer={handleRetryTransfer}
          onRetryAll={handleRetryAll}
          onSelectTransfer={setSelectedTransfer}
          onClearRetries={handleClearRetries}
          onRefresh={loadData}
        />
      )}

      {activeTab === 'explorer' && (
        <SftpExplorer workflows={workflows} />
      )}

      {activeTab === 'verify' && (
        <VerifySync workflows={workflows} />
      )}

      {activeTab === 'logs' && (
        <LogViewer
          logs={logs}
          onRefresh={loadData}
        />
      )}

      {/* Transfer Audit Lifecycle Modal */}
      {selectedTransfer && (
        <Modal
          transfer={selectedTransfer}
          transfers={transfers}
          onClose={() => setSelectedTransfer(null)}
        />
      )}

      {/* Observation / Dry-Run Simulation Modal */}
      {observeWorkflow && (
        <ObservationModal
          workflow={observeWorkflow}
          onClose={() => setObserveWorkflow(null)}
          onRefreshData={loadData}
        />
      )}

      {/* Job Editor / Creation Modal */}
      {(editingJob || isCreatingJob) && (
        <JobEditorModal
          workflow={editingJob}
          onClose={() => {
            setEditingJob(null);
            setIsCreatingJob(false);
          }}
          onSave={handleSaveJob}
          onDelete={handleDeleteJob}
        />
      )}

      {/* One-time code entry when a portal sign-in (SCAN) is waiting for one; sits above every other modal */}
      <PortalCodePrompt />

      </div>
    </div>
  );
}
