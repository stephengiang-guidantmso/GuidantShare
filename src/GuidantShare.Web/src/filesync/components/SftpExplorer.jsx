import React, { useState, useEffect, useRef } from 'react';
import { browseWorkflowFolder } from '../fsApi';

const SELECTED_JOB_KEY = 'sftpExplorer.workflowId';

const FolderIcon = () => (
  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
    <path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"></path>
  </svg>
);

const FileIcon = () => (
  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
    <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>
    <polyline points="14 2 14 8 20 8"></polyline>
  </svg>
);

const formatBytes = (bytes) => {
  if (!bytes || bytes === 0) return '0 B';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.min(Math.floor(Math.log(bytes) / Math.log(k)), sizes.length - 1);
  return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
};

const formatDate = (iso) => {
  if (!iso) return '-';
  const d = new Date(iso);
  return `${d.toLocaleDateString([], { year: 'numeric', month: 'short', day: 'numeric' })} ${d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`;
};

const readSavedJob = () => {
  try { return localStorage.getItem(SELECTED_JOB_KEY) || ''; } catch { return ''; }
};

/**
 * Read-only SFTP browser scoped to one job's SFTP folder.
 * Shows folder and file names with size and modified date. Files cannot be opened, downloaded, or changed.
 */
export default function SftpExplorer({ workflows = [] }) {
  const [workflowId, setWorkflowId] = useState(readSavedJob);
  const [path, setPath] = useState('');
  const [listing, setListing] = useState(null);
  const [error, setError] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  const [sort, setSort] = useState({ key: 'name', asc: true });
  const requestId = useRef(0);

  // Fall back to the first job when nothing (or a deleted job) is selected
  useEffect(() => {
    if (workflows.length === 0) return;
    if (!workflows.some(wf => wf.workflowId === workflowId)) {
      setWorkflowId(workflows[0].workflowId);
      setPath('');
    }
  }, [workflows, workflowId]);

  const loadFolder = async (wfId, folderPath) => {
    if (!wfId) return;
    const thisRequest = ++requestId.current;
    setIsLoading(true);
    setError('');
    try {
      const data = await browseWorkflowFolder(wfId, folderPath);
      if (thisRequest !== requestId.current) return; // a newer navigation already started
      setListing(data);
    } catch (err) {
      if (thisRequest !== requestId.current) return;
      setListing(null);
      setError(err.message || 'Could not open this folder.');
    } finally {
      if (thisRequest === requestId.current) setIsLoading(false);
    }
  };

  useEffect(() => {
    loadFolder(workflowId, path);
  }, [workflowId, path]);

  const selectJob = (id) => {
    setWorkflowId(id);
    setPath('');
    try { localStorage.setItem(SELECTED_JOB_KEY, id); } catch { /* storage unavailable */ }
  };

  const segments = path ? path.split('/') : [];
  const openFolder = (name) => setPath(path ? `${path}/${name}` : name);
  const goToSegment = (index) => setPath(segments.slice(0, index + 1).join('/'));
  const goUp = () => setPath(segments.slice(0, -1).join('/'));

  const toggleSort = (key) => {
    setSort(prev => (prev.key === key ? { key, asc: !prev.asc } : { key, asc: true }));
  };

  // Folders always stay above files; the chosen column sorts within each group
  const entries = [...(listing?.entries || [])].sort((a, b) => {
    if (a.isDirectory !== b.isDirectory) return a.isDirectory ? -1 : 1;
    let result = 0;
    if (sort.key === 'size') result = (a.size || 0) - (b.size || 0);
    else if (sort.key === 'modified') result = new Date(a.lastModifiedUtc) - new Date(b.lastModifiedUtc);
    if (result === 0) result = a.name.localeCompare(b.name, undefined, { sensitivity: 'base', numeric: true });
    return sort.asc ? result : -result;
  });

  const selectedJob = workflows.find(wf => wf.workflowId === workflowId);
  const rootLabel = listing?.rootPath || selectedJob?.sftpRemotePath || '/';
  const folderCount = entries.filter(e => e.isDirectory).length;
  const fileCount = entries.length - folderCount;

  const sortIndicator = (key) => (sort.key !== key ? null : (
    <svg className="explorer-sort-icon" width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round" aria-label={sort.asc ? 'ascending' : 'descending'}>
      <polyline points={sort.asc ? '6 15 12 9 18 15' : '6 9 12 15 18 9'}></polyline>
    </svg>
  ));

  return (
    <div id="sftp-explorer-section" style={{ marginBottom: '28px' }}>
      <div className="section-header">
        <div className="section-title">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"></path>
          </svg>
          <span>SFTP EXPLORER (READ-ONLY)</span>
        </div>
      </div>

      <div className="ledger-filter-bar">
        <label className="ledger-filter-field">
          <span>Job</span>
          <select id="sftp-explorer-job" value={workflowId} onChange={e => selectJob(e.target.value)}>
            {workflows.length === 0 && <option value="">No jobs configured</option>}
            {workflows.map(wf => (
              <option key={wf.workflowId} value={wf.workflowId}>{wf.name}</option>
            ))}
          </select>
        </label>
        <div className="ledger-filter-summary">
          {selectedJob && <span>Server: {selectedJob.sftpServerId}</span>}
        </div>
      </div>

      <div className="content-card">
        <div className="explorer-toolbar">
          <nav className="explorer-breadcrumb" aria-label="Folder path">
            <button className="explorer-crumb" onClick={() => setPath('')} disabled={segments.length === 0}>
              {rootLabel}
            </button>
            {segments.map((seg, i) => (
              <React.Fragment key={`${i}-${seg}`}>
                <span className="explorer-crumb-sep">/</span>
                <button className="explorer-crumb" onClick={() => goToSegment(i)} disabled={i === segments.length - 1}>
                  {seg}
                </button>
              </React.Fragment>
            ))}
          </nav>
          <span className="explorer-count">
            {isLoading ? 'Loading...' : listing ? `${folderCount} folder${folderCount === 1 ? '' : 's'}, ${fileCount} file${fileCount === 1 ? '' : 's'}` : ''}
          </span>
        </div>

        <div className="table-container">
            <table id="sftp-explorer-table">
              <thead>
                <tr>
                  <th className="explorer-sortable" onClick={() => toggleSort('name')}>Name{sortIndicator('name')}</th>
                  <th className="explorer-sortable" onClick={() => toggleSort('size')}>Size{sortIndicator('size')}</th>
                  <th className="explorer-sortable" onClick={() => toggleSort('modified')}>Modified{sortIndicator('modified')}</th>
                </tr>
              </thead>
              <tbody style={{ opacity: isLoading ? 0.5 : 1 }}>
                {segments.length > 0 && (
                  <tr className="explorer-row-folder">
                    <td>
                      <button id="sftp-explorer-up" className="explorer-name explorer-name-folder" onClick={goUp} disabled={isLoading} title="Go up one folder">
                        <FolderIcon />
                        <span>..</span>
                      </button>
                    </td>
                    <td style={{ fontSize: '0.82rem', fontFamily: 'var(--font-mono)', color: 'var(--text-muted)' }}>-</td>
                    <td style={{ fontSize: '0.82rem', fontFamily: 'var(--font-mono)', color: 'var(--text-muted)' }}>-</td>
                  </tr>
                )}
                {entries.map(entry => (
                  <tr key={entry.name} className={entry.isDirectory ? 'explorer-row-folder' : ''}>
                    <td>
                      {entry.isDirectory ? (
                        <button className="explorer-name explorer-name-folder" onClick={() => openFolder(entry.name)} disabled={isLoading}>
                          <FolderIcon />
                          <span>{entry.name}</span>
                        </button>
                      ) : (
                        <span className="explorer-name">
                          <FileIcon />
                          <span>{entry.name}</span>
                        </span>
                      )}
                    </td>
                    <td style={{ fontSize: '0.82rem', fontFamily: 'var(--font-mono)', color: 'var(--text-muted)' }}>
                      {entry.isDirectory ? '-' : formatBytes(entry.size)}
                    </td>
                    <td style={{ fontSize: '0.82rem', fontFamily: 'var(--font-mono)', color: 'var(--text-muted)' }}>
                      {formatDate(entry.lastModifiedUtc)}
                    </td>
                  </tr>
                ))}
                {error && (
                  <tr>
                    <td colSpan={3} className="explorer-message explorer-message-error">{error}</td>
                  </tr>
                )}
                {!error && !isLoading && listing && entries.length === 0 && (
                  <tr>
                    <td colSpan={3} style={{ textAlign: 'center', color: 'var(--text-muted)', fontSize: '0.85rem', padding: '20px' }}>
                      This folder is empty.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
      </div>
    </div>
  );
}
