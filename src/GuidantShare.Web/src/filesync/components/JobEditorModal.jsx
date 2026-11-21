import React, { useState, useEffect } from 'react';
import { verifySftpConnection } from '../fsApi';
import { ENDPOINT_TYPES, isPortal as isPortalType } from '../endpointTypes';
import { overlayCloseProps } from './overlayClose';
import { useConfirmClick } from './confirmClick';

const inputStyle = {
  width: '100%',
  padding: '8px 10px',
  borderRadius: '8px',
  border: '1px solid var(--card-border)',
  background: 'var(--gray-bg)',
  color: 'var(--text-main)',
  fontSize: '0.88rem'
};

export default function JobEditorModal({ workflow, onClose, onSave, onDelete }) {
  const isEditing = Boolean(workflow && workflow.workflowId);

  const [formData, setFormData] = useState({
    name: '',
    plan: '',
    folderLabel: '',
    workflowId: '',
    direction: 'SftpToShareFile',
    endpointType: 'Sftp',
    pollingIntervalSeconds: 180,
    stabilityPeriodSeconds: 60,
    sftpRemotePath: '/',
    shareFileFolderId: '',
    shareFileDisplayPath: '',
    includePatterns: '*.*',
    excludePatterns: '*.tmp, *.part, ~*',
    sftpHost: '',
    sftpPort: 22,
    sftpUsername: '',
    sftpPassword: '',
    enableMirrorPurge: true,
    checkDeleteProbe: true,
    existingFoldersOnly: false,
    verifyDestination: null // null = direction default (on for SFTP -> ShareFile, off for ShareFile -> SFTP)
  });

  // Saved passwords are write-only: the server never sends them back. Typing here replaces the saved one; blank keeps it.
  const [showPassword, setShowPassword] = useState(false);
  const hasSavedPassword = Boolean(isEditing && workflow?.sftpConfig?.hasPassword);
  const [isSaving, setIsSaving] = useState(false);
  const [isTesting, setIsTesting] = useState(false);
  const [testResult, setTestResult] = useState(null);
  const [errorMsg, setErrorMsg] = useState('');

  const [deleteArmed, confirmDelete] = useConfirmClick();

  const handleDelete = () => confirmDelete(async () => {
    setIsSaving(true);
    setErrorMsg('');
    try {
      if (onDelete && workflow?.workflowId) {
        await onDelete(workflow.workflowId);
      }
      onClose();
    } catch (err) {
      console.error('Delete failed:', err);
      setErrorMsg('Failed to delete Transfer Job.');
    } finally {
      setIsSaving(false);
    }
  });

  useEffect(() => {
    if (workflow) {
      setFormData({
        name: workflow.name || '',
        plan: workflow.plan || '',
        folderLabel: workflow.folderLabel || '',
        workflowId: workflow.workflowId || '',
        direction: workflow.direction || 'SftpToShareFile',
        endpointType: workflow.endpointType || 'Sftp',
        pollingIntervalSeconds: workflow.pollingIntervalSeconds || 180,
        stabilityPeriodSeconds: workflow.stabilityPeriodSeconds || 60,
        sftpRemotePath: workflow.sftpRemotePath || '/',
        shareFileFolderId: workflow.shareFileFolderId || '',
        shareFileDisplayPath: workflow.shareFileDisplayPath || '',
        includePatterns: Array.isArray(workflow.includePatterns) ? workflow.includePatterns.join(', ') : (workflow.includePatterns || '*.*'),
        excludePatterns: Array.isArray(workflow.excludePatterns) ? workflow.excludePatterns.join(', ') : (workflow.excludePatterns || '*.tmp, ~*'),
        sftpHost: workflow.sftpConfig?.host || '',
        sftpPort: workflow.sftpConfig?.port || 22,
        sftpUsername: workflow.sftpConfig?.username || '',
        sftpPassword: '', // never loaded from the server; blank on save keeps the saved password
        enableMirrorPurge: workflow.enableMirrorPurge !== undefined ? Boolean(workflow.enableMirrorPurge) : true,
        checkDeleteProbe: workflow.checkDeleteProbe !== undefined ? Boolean(workflow.checkDeleteProbe) : true,
        verifyDestination: workflow.verifyDestination ?? null,
        existingFoldersOnly: Boolean(workflow.existingFoldersOnly)
      });
    }
  }, [workflow]);

  const handleChange = (e) => {
    const { name, value } = e.target;
    setFormData(prev => ({ ...prev, [name]: value }));
  };

  // Switching the remote type prefills the portal URL and the safe defaults for portals
  const handleEndpointTypeChange = (e) => {
    const endpointType = e.target.value;
    setTestResult(null);
    setFormData(prev => {
      const next = { ...prev, endpointType };
      const knownUrls = Object.values(ENDPOINT_TYPES).map(t => t.defaultUrl).filter(Boolean);
      if (isPortalType(endpointType)) {
        if (!prev.sftpHost || prev.sftpHost === 'sftp.partner.com' || knownUrls.includes(prev.sftpHost)) {
          next.sftpHost = ENDPOINT_TYPES[endpointType].defaultUrl;
        }
        // Portals remove or consume files themselves; never delete our copies because of that
        next.enableMirrorPurge = false;
        if (ENDPOINT_TYPES[endpointType].downloadOnly) next.direction = 'SftpToShareFile';
      } else if (knownUrls.includes(prev.sftpHost)) {
        next.sftpHost = '';
      }
      return next;
    });
  };

  const buildSftpConfigPayload = () => ({
    host: formData.sftpHost.trim(),
    port: parseInt(formData.sftpPort, 10) || 22,
    username: formData.sftpUsername.trim(),
    password: formData.sftpPassword,
    connectionTimeoutSeconds: 8
  });

  const handleTestConnection = async () => {
    if (!formData.sftpHost.trim()) {
      setTestResult({ success: false, message: isPortal ? 'Please enter the portal URL first.' : 'Please enter an SFTP Host address first.' });
      return;
    }
    if (!formData.sftpUsername.trim() && formData.endpointType !== 'FilesCom') {
      setTestResult({ success: false, message: isPortal ? 'Please enter the portal username first.' : 'Please enter an SFTP Username first.' });
      return;
    }

    setIsTesting(true);
    setTestResult(null);
    setErrorMsg('');

    try {
      const sftpPayload = buildSftpConfigPayload();
      const res = await verifySftpConnection(sftpPayload, isEditing ? workflow.workflowId : null, formData.endpointType);
      setTestResult({
        success: true,
        message: res.message || 'Connection and sign-in succeeded!'
      });
    } catch (err) {
      setTestResult({
        success: false,
        message: err.message || (isPortal
          ? 'Portal sign-in failed. Check the URL and credentials.'
          : 'SFTP connection failed. Check host, port, credentials, and network reachability.')
      });
    } finally {
      setIsTesting(false);
    }
  };

  const handleSubmit = async (e) => {
    e.preventDefault();
    if (!formData.name.trim()) {
      setErrorMsg('Job Title is required');
      return;
    }
    if (!formData.shareFileFolderId.trim()) {
      setErrorMsg('ShareFile Folder ID is required (e.g. fo65123d-e7a9-4bec-9c32-89c813df9964)');
      return;
    }

    setIsSaving(true);
    setErrorMsg('');
    setTestResult(null);

    const sftpPayload = buildSftpConfigPayload();

    // 1. Mandatory connection check before saving
    try {
      await verifySftpConnection(sftpPayload, isEditing ? workflow.workflowId : null, formData.endpointType);
    } catch (verifyErr) {
      const msg = verifyErr.message || 'Connection test failed';
      setErrorMsg(`Connection Test Failed: ${msg}. Changes cannot be saved until the connection test succeeds.`);
      setTestResult({ success: false, message: msg });
      setIsSaving(false);
      return;
    }

    // 2. Build workflow payload
    const payload = {
      name: formData.name.trim(),
      plan: formData.plan.trim(),
      folderLabel: formData.folderLabel.trim(),
      direction: formData.direction,
      endpointType: formData.endpointType,
      pollingIntervalSeconds: parseInt(formData.pollingIntervalSeconds, 10) || 180,
      stabilityPeriodSeconds: parseInt(formData.stabilityPeriodSeconds, 10) || 60,
      sftpRemotePath: formData.sftpRemotePath.trim(),
      shareFileFolderId: formData.shareFileFolderId.trim(),
      shareFileDisplayPath: formData.shareFileDisplayPath.trim() || formData.name.trim() || formData.shareFileFolderId.trim(),
      includePatterns: formData.includePatterns ? formData.includePatterns.split(',').map(s => s.trim()).filter(Boolean) : ['*.*'],
      excludePatterns: formData.excludePatterns.split(',').map(s => s.trim()).filter(Boolean),
      sftpConfig: sftpPayload,
      isEnabled: isEditing ? (workflow.isEnabled ?? false) : false,
      enableMirrorPurge: formData.enableMirrorPurge !== undefined ? Boolean(formData.enableMirrorPurge) : true,
      checkDeleteProbe: formData.checkDeleteProbe !== undefined ? Boolean(formData.checkDeleteProbe) : true,
      verifyDestination: formData.verifyDestination,
      existingFoldersOnly: isUpload && Boolean(formData.existingFoldersOnly)
    };

    if (isEditing) {
      payload.workflowId = workflow.workflowId;
      // Keep the job on its existing SFTP credentials entry, even if it was renamed
      payload.sftpServerId = workflow.sftpServerId;
      // Preserve settings this form doesn't edit
      payload.cronSchedule = workflow.cronSchedule;
      payload.maxRetryAttempts = workflow.maxRetryAttempts;
      payload.initialRetryIntervalSeconds = workflow.initialRetryIntervalSeconds;
    }

    try {
      await onSave(payload, isEditing ? workflow.workflowId : null);
      onClose();
    } catch (err) {
      console.error('Save failed:', err);
      setErrorMsg(err.message || 'Failed to save Transfer Job. Please check your inputs.');
    } finally {
      setIsSaving(false);
    }
  };

  const isUpload = formData.direction === 'ShareFileToSftp';
  const isPortal = isPortalType(formData.endpointType);
  const endpointInfo = ENDPOINT_TYPES[formData.endpointType] || ENDPOINT_TYPES.Sftp;
  const remoteName = isPortal ? endpointInfo.shortName : 'SFTP';
  const remoteServer = isPortal ? `the ${endpointInfo.shortName} portal` : 'the SFTP server';
  const sourceName = isUpload ? 'ShareFile' : remoteServer;
  const targetName = isUpload ? remoteServer : 'ShareFile';

  return (
    <div className="modal-overlay" id="job-editor-overlay" {...overlayCloseProps(onClose)}>
      <div 
        className="modal-content" 
        id="job-editor-content" 
        onClick={(e) => e.stopPropagation()}
        style={{ maxWidth: '780px', width: '92%', maxHeight: '90vh', overflowY: 'auto' }}
      >
        {/* Header */}
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '16px', borderBottom: '1px solid var(--card-border)', paddingBottom: '12px' }}>
          <div>
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
              <span className="status-badge badge-blue">
                {isEditing ? 'EDIT TRANSFER JOB' : '+ NEW TRANSFER JOB'}
              </span>
              <h3 style={{ fontSize: '1.25rem', fontWeight: 700, color: 'var(--text-main)', margin: 0 }}>
                {isEditing ? formData.name || 'Edit Configuration' : 'Create New Transfer Job'}
              </h3>
            </div>
            <div style={{ fontSize: '0.8rem', color: 'var(--text-muted)', marginTop: '4px' }}>
              Configure client SFTP server credentials, ShareFile cloud destinations, and automated sync schedules.
            </div>
          </div>
          <button 
            id="btn-close-editor-modal"
            className="btn btn-secondary btn-sm" 
            onClick={onClose}
          >
            Close
          </button>
        </div>

        {errorMsg && (
          <div style={{ background: 'var(--red-bg)', border: '1px solid var(--red-border)', color: 'var(--red-text)', padding: '10px 14px', borderRadius: '8px', marginBottom: '16px', fontSize: '0.85rem' }}>
            {errorMsg}
          </div>
        )}

        <form onSubmit={handleSubmit}>
          {/* SECTION 1: GENERAL JOB SETTINGS */}
          <div style={{ marginBottom: '18px' }}>
            <div style={{ fontSize: '0.82rem', fontWeight: 700, color: 'var(--primary-blue)', textTransform: 'uppercase', letterSpacing: '0.04em', marginBottom: '10px' }}>
              1. General Transfer Job Info
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '14px', marginBottom: '12px' }}>
              <div>
                <label style={{ display: 'block', fontSize: '0.78rem', fontWeight: 600, color: 'var(--text-secondary)', marginBottom: '4px' }}>
                  Website Health Plan
                </label>
                <input type="text" name="plan" value={formData.plan} onChange={handleChange}
                  placeholder="e.g. Clever Care" style={inputStyle} />
                <span style={{ fontSize: '0.72rem', color: 'var(--text-muted)' }}>Jobs with the same plan show as one plan on the website. Blank = not shown to users.</span>
              </div>
              <div>
                <label style={{ display: 'block', fontSize: '0.78rem', fontWeight: 600, color: 'var(--text-secondary)', marginBottom: '4px' }}>
                  Website Folder Name
                </label>
                <input type="text" name="folderLabel" value={formData.folderLabel} onChange={handleChange}
                  placeholder="e.g. 278 (blank = the plan name)" style={inputStyle} />
                <span style={{ fontSize: '0.72rem', color: 'var(--text-muted)' }}>Same plan + same direction + same folder name = one folder, even across several logins. Only enabled jobs are shown.</span>
              </div>
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: '2fr 1fr', gap: '14px', marginBottom: '12px' }}>
              <div>
                <label style={{ display: 'block', fontSize: '0.78rem', fontWeight: 600, color: 'var(--text-secondary)', marginBottom: '4px' }}>
                  Job Title / Partner Name *
                </label>
                <input
                  type="text"
                  name="name"
                  value={formData.name}
                  onChange={handleChange}
                  placeholder="e.g. Partner A SFTP to ShareFile"
                  required
                  style={{
                    width: '100%',
                    padding: '8px 12px',
                    borderRadius: '8px',
                    border: '1px solid var(--card-border)',
                    background: 'var(--gray-bg)',
                    color: 'var(--text-main)',
                    fontSize: '0.88rem'
                  }}
                />
              </div>

              <div>
                <label style={{ display: 'block', fontSize: '0.78rem', fontWeight: 600, color: 'var(--text-secondary)', marginBottom: '4px' }}>
                  Direction *
                </label>
                <select
                  name="direction"
                  value={formData.direction}
                  onChange={handleChange}
                  style={{
                    width: '100%',
                    padding: '8px 10px',
                    borderRadius: '8px',
                    border: '1px solid var(--card-border)',
                    background: 'var(--gray-bg)',
                    color: 'var(--text-main)',
                    fontSize: '0.88rem'
                  }}
                >
                  <option value="SftpToShareFile">{remoteName} → ShareFile (Inbound)</option>
                  <option value="ShareFileToSftp" disabled={Boolean(endpointInfo.downloadOnly)}>ShareFile → {remoteName} (Outbound)</option>
                </select>
              </div>
            </div>

            <div style={{ marginBottom: '12px' }}>
              <label style={{ display: 'block', fontSize: '0.78rem', fontWeight: 600, color: 'var(--text-secondary)', marginBottom: '4px' }}>
                Remote Type *
              </label>
              <select
                name="endpointType"
                id="sel-endpoint-type"
                value={formData.endpointType}
                onChange={handleEndpointTypeChange}
                style={inputStyle}
              >
                {Object.entries(ENDPOINT_TYPES).map(([value, info]) => (
                  <option key={value} value={value}>{info.label}</option>
                ))}
              </select>
              {isPortal && (
                <span style={{ fontSize: '0.72rem', color: 'var(--text-muted)' }}>
                  Web portal: FileSync signs in like a person would and transfers files through the portal's own API.
                  {formData.endpointType === 'ScanPortal' && ' When SCAN emails a one-time code, it is read from the mailbox set in data\\graph-mail.json.'}
                </span>
              )}
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '14px' }}>
              <div>
                <label style={{ display: 'block', fontSize: '0.78rem', fontWeight: 600, color: 'var(--text-secondary)', marginBottom: '4px' }}>
                  Polling Schedule (Seconds) *
                </label>
                <input
                  type="number"
                  name="pollingIntervalSeconds"
                  value={formData.pollingIntervalSeconds}
                  onChange={handleChange}
                  min="30"
                  step="10"
                  required
                  style={{
                    width: '100%',
                    padding: '8px 12px',
                    borderRadius: '8px',
                    border: '1px solid var(--card-border)',
                    background: 'var(--gray-bg)',
                    color: 'var(--text-main)',
                    fontFamily: 'var(--font-mono)',
                    fontSize: '0.88rem'
                  }}
                />
                <span style={{ fontSize: '0.72rem', color: 'var(--text-muted)' }}>How often FileSync checks the remote server (e.g. 180s = 3 mins)</span>
              </div>

              <div>
                <label style={{ display: 'block', fontSize: '0.78rem', fontWeight: 600, color: 'var(--text-secondary)', marginBottom: '4px' }}>
                  Stability Delay (Seconds) *
                </label>
                <input
                  type="number"
                  name="stabilityPeriodSeconds"
                  value={formData.stabilityPeriodSeconds}
                  onChange={handleChange}
                  min="10"
                  step="10"
                  required
                  style={{
                    width: '100%',
                    padding: '8px 12px',
                    borderRadius: '8px',
                    border: '1px solid var(--card-border)',
                    background: 'var(--gray-bg)',
                    color: 'var(--text-main)',
                    fontFamily: 'var(--font-mono)',
                    fontSize: '0.88rem'
                  }}
                />
                <span style={{ fontSize: '0.72rem', color: 'var(--text-muted)' }}>Safety window ensuring client finished uploading (default 60s)</span>
              </div>
            </div>
          </div>

          {/* SECTION 2: SFTP SERVER CREDENTIALS */}
          <div style={{ marginBottom: '18px', borderTop: '1px solid var(--card-border)', paddingTop: '14px' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '10px' }}>
              <div style={{ fontSize: '0.82rem', fontWeight: 700, color: 'var(--primary-blue)', textTransform: 'uppercase', letterSpacing: '0.04em' }}>
                {isPortal ? `2. ${endpointInfo.shortName} Portal Sign-in` : '2. Partner SFTP Server & Credentials'}
              </div>
              <button
                type="button"
                id="btn-test-sftp-connection"
                className="btn btn-secondary btn-sm"
                onClick={handleTestConnection}
                disabled={isTesting || isSaving}
                style={{ fontSize: '0.75rem', padding: '4px 10px', display: 'inline-flex', alignItems: 'center', gap: '5px' }}
                title={isPortal ? 'Sign in to the portal with these credentials' : 'Verify network connection and login credentials against the remote SFTP server'}
              >
                {isTesting ? (isPortal ? 'Signing in...' : 'Pinging SFTP...') : 'Test Connection'}
              </button>
            </div>

            {testResult && (
              <div style={{
                background: testResult.success ? 'rgba(34, 197, 94, 0.12)' : 'var(--red-bg)',
                border: `1px solid ${testResult.success ? 'rgba(34, 197, 94, 0.3)' : 'var(--red-border)'}`,
                color: testResult.success ? 'var(--green-text)' : 'var(--red-text)',
                padding: '8px 12px',
                borderRadius: '8px',
                marginBottom: '12px',
                fontSize: '0.82rem',
                display: 'flex',
                alignItems: 'center',
                gap: '8px'
              }}>
                <span>{testResult.message}</span>
              </div>
            )}

            <div style={{ display: 'grid', gridTemplateColumns: isPortal ? '1fr' : '2fr 1fr', gap: '14px', marginBottom: '12px' }}>
              <div>
                <label style={{ display: 'block', fontSize: '0.78rem', fontWeight: 600, color: 'var(--text-secondary)', marginBottom: '4px' }}>
                  {isPortal ? 'Portal URL *' : 'SFTP Host Address *'}
                </label>
                <input
                  type="text"
                  name="sftpHost"
                  value={formData.sftpHost}
                  onChange={handleChange}
                  placeholder={isPortal ? endpointInfo.defaultUrl : 'e.g. 127.0.0.1 or sftp.partner.example.com'}
                  required
                  style={{
                    width: '100%',
                    padding: '8px 12px',
                    borderRadius: '8px',
                    border: '1px solid var(--card-border)',
                    background: 'var(--gray-bg)',
                    color: 'var(--text-main)',
                    fontFamily: 'var(--font-mono)',
                    fontSize: '0.88rem'
                  }}
                />
              </div>

              {!isPortal && (
              <div>
                <label style={{ display: 'block', fontSize: '0.78rem', fontWeight: 600, color: 'var(--text-secondary)', marginBottom: '4px' }}>
                  Port
                </label>
                <input
                  type="number"
                  name="sftpPort"
                  value={formData.sftpPort}
                  onChange={handleChange}
                  style={{
                    width: '100%',
                    padding: '8px 12px',
                    borderRadius: '8px',
                    border: '1px solid var(--card-border)',
                    background: 'var(--gray-bg)',
                    color: 'var(--text-main)',
                    fontFamily: 'var(--font-mono)',
                    fontSize: '0.88rem'
                  }}
                />
              </div>
              )}
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '14px', marginBottom: '12px' }}>
              <div>
                <label style={{ display: 'block', fontSize: '0.78rem', fontWeight: 600, color: 'var(--text-secondary)', marginBottom: '4px' }}>
                  {isPortal ? `${endpointInfo.shortName} Username${formData.endpointType === 'FilesCom' ? '' : ' *'}` : 'SFTP Username *'}
                </label>
                <input
                  type="text"
                  name="sftpUsername"
                  value={formData.sftpUsername}
                  onChange={handleChange}
                  placeholder={formData.endpointType === 'FilesCom' ? 'Blank = password field holds an API key' : (isPortal ? 'Portal sign-in name' : 'e.g. sftp_username')}
                  required={formData.endpointType !== 'FilesCom'}
                  style={{
                    width: '100%',
                    padding: '8px 12px',
                    borderRadius: '8px',
                    border: '1px solid var(--card-border)',
                    background: 'var(--gray-bg)',
                    color: 'var(--text-main)',
                    fontFamily: 'var(--font-mono)',
                    fontSize: '0.88rem'
                  }}
                />
              </div>

              <div>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '4px' }}>
                  <label style={{ fontSize: '0.78rem', fontWeight: 600, color: 'var(--text-secondary)' }}>
                    {isPortal ? `${endpointInfo.shortName} Password` : 'SFTP Password / Secret'}
                  </label>
                  <button
                    type="button"
                    onClick={() => setShowPassword(v => !v)}
                    style={{ background: 'none', border: 'none', color: 'var(--primary-blue)', fontSize: '0.75rem', cursor: 'pointer', padding: 0 }}
                  >
                    {showPassword ? 'Hide typed text' : 'Show typed text'}
                  </button>
                </div>
                <input
                  type={showPassword ? 'text' : 'password'}
                  name="sftpPassword"
                  value={formData.sftpPassword}
                  onChange={handleChange}
                  placeholder={hasSavedPassword ? 'A password is saved (leave blank to keep it, or type to replace)' : (isPortal ? 'Enter portal password' : 'Enter partner SFTP password')}
                  style={{
                    width: '100%',
                    padding: '8px 12px',
                    borderRadius: '8px',
                    border: '1px solid var(--card-border)',
                    background: 'var(--gray-bg)',
                    color: 'var(--text-main)',
                    fontFamily: 'var(--font-mono)',
                    fontSize: '0.88rem'
                  }}
                />
              </div>
            </div>

            <div style={{ marginBottom: '12px' }}>
              <label style={{ display: 'block', fontSize: '0.78rem', fontWeight: 600, color: 'var(--text-secondary)', marginBottom: '4px' }}>
                {isPortal
                  ? (isUpload ? `${endpointInfo.shortName} Destination Folder *` : `${endpointInfo.shortName} Folder *`)
                  : (isUpload ? 'Remote SFTP Destination Folder Path *' : 'Remote SFTP Folder Path *')}
              </label>
              <input
                type="text"
                name="sftpRemotePath"
                value={formData.sftpRemotePath}
                onChange={handleChange}
                placeholder={isPortal
                  ? `e.g. ${(isUpload ? endpointInfo.folderExample?.upload : endpointInfo.folderExample?.download) || '/'}`
                  : (isUpload ? "e.g. /Incoming/278 or /toSCAN" : "e.g. / or /outgoing or .")}
                required
                style={{
                  width: '100%',
                  padding: '8px 12px',
                  borderRadius: '8px',
                  border: '1px solid var(--card-border)',
                  background: 'var(--gray-bg)',
                  color: 'var(--text-main)',
                  fontFamily: 'var(--font-mono)',
                  fontSize: '0.88rem'
                }}
              />
            </div>

            {!isPortal && (
              <div style={{ fontSize: '0.74rem', color: 'var(--text-muted)' }}>
                Server identity (host key) is saved automatically the first time FileSync connects; after that, a server presenting a different key is refused.
              </div>
            )}
          </div>

          {/* SECTION 3: SHAREFILE SOURCE/DESTINATION & PATTERNS */}
          <div style={{ marginBottom: '22px', borderTop: '1px solid var(--card-border)', paddingTop: '14px' }}>
            <div style={{ fontSize: '0.82rem', fontWeight: 700, color: 'var(--primary-blue)', textTransform: 'uppercase', letterSpacing: '0.04em', marginBottom: '10px' }}>
              {isUpload ? '3. ShareFile Source & File Masks' : '3. ShareFile Destination & File Masks'}
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '14px', marginBottom: '12px' }}>
              <div>
                <label style={{ display: 'block', fontSize: '0.78rem', fontWeight: 600, color: 'var(--text-secondary)', marginBottom: '4px' }}>
                  {isUpload ? 'ShareFile Source Folder ID *' : 'ShareFile Destination Folder ID *'}
                </label>
                <input
                  type="text"
                  name="shareFileFolderId"
                  value={formData.shareFileFolderId}
                  onChange={handleChange}
                  placeholder="e.g. fo65123d-e7a9-4bec-9c32-89c813df9964"
                  required
                  style={{
                    width: '100%',
                    padding: '8px 12px',
                    borderRadius: '8px',
                    border: '1px solid var(--card-border)',
                    background: 'var(--gray-bg)',
                    color: 'var(--text-main)',
                    fontFamily: 'var(--font-mono)',
                    fontSize: '0.88rem'
                  }}
                />
              </div>

              <div>
                <label style={{ display: 'block', fontSize: '0.78rem', fontWeight: 600, color: 'var(--text-secondary)', marginBottom: '4px' }}>
                  {isUpload ? 'ShareFile Source Display Path (Optional)' : 'ShareFile Destination Display Path (Optional)'}
                </label>
                <input
                  type="text"
                  name="shareFileDisplayPath"
                  value={formData.shareFileDisplayPath}
                  onChange={handleChange}
                  placeholder="e.g. Shared Folders/Test"
                  style={{
                    width: '100%',
                    padding: '8px 12px',
                    borderRadius: '8px',
                    border: '1px solid var(--card-border)',
                    background: 'var(--gray-bg)',
                    color: 'var(--text-main)',
                    fontSize: '0.88rem'
                  }}
                />
              </div>
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '14px' }}>
              <div>
                <label style={{ display: 'block', fontSize: '0.78rem', fontWeight: 600, color: 'var(--text-secondary)', marginBottom: '4px' }}>
                  Include Patterns (Comma-separated)
                </label>
                <input
                  type="text"
                  name="includePatterns"
                  value={formData.includePatterns}
                  onChange={handleChange}
                  placeholder="*.*"
                  style={{
                    width: '100%',
                    padding: '8px 12px',
                    borderRadius: '8px',
                    border: '1px solid var(--card-border)',
                    background: 'var(--gray-bg)',
                    color: 'var(--text-main)',
                    fontFamily: 'var(--font-mono)',
                    fontSize: '0.84rem'
                  }}
                />
              </div>

              <div>
                <label style={{ display: 'block', fontSize: '0.78rem', fontWeight: 600, color: 'var(--text-secondary)', marginBottom: '4px' }}>
                  Exclude Patterns (Lockfiles / Temp)
                </label>
                <input
                  type="text"
                  name="excludePatterns"
                  value={formData.excludePatterns}
                  onChange={handleChange}
                  placeholder="*.tmp, *.part, ~*"
                  style={{
                    width: '100%',
                    padding: '8px 12px',
                    borderRadius: '8px',
                    border: '1px solid var(--card-border)',
                    background: 'var(--gray-bg)',
                    color: 'var(--text-main)',
                    fontFamily: 'var(--font-mono)',
                    fontSize: '0.84rem'
                  }}
                />
              </div>
            </div>

            {/* SYNC OPTIONS: plain-language switches, worded with this job's real source and target */}
            <div style={{ marginTop: '18px', fontSize: '0.82rem', fontWeight: 700, color: 'var(--primary-blue)', textTransform: 'uppercase', letterSpacing: '0.04em' }}>
              Sync Options
            </div>

            <OptionRow
              id="chk-enable-mirror-purge"
              title="Delete from target when deleted from source"
              checked={Boolean(formData.enableMirrorPurge) && !(isUpload && formData.existingFoldersOnly)}
              disabled={isUpload && Boolean(formData.existingFoldersOnly)}
              onChange={(checked) => setFormData(prev => ({ ...prev, enableMirrorPurge: checked }))}
            >
              When a file disappears from {sourceName}, FileSync deletes the copy it put in {targetName}. Only files FileSync delivered are ever deleted.
              {' '}Keep this off for web portals and partner drop-boxes, which clear out files on their own.
              {isUpload && formData.existingFoldersOnly && ' Not available while "Only use folders that already exist on target" is on.'}
            </OptionRow>

            {isUpload && !isPortal && Boolean(formData.enableMirrorPurge) && !formData.existingFoldersOnly && (
              <div style={{ marginLeft: '28px' }}>
                <OptionRow
                  id="chk-check-delete-probe"
                  title="Check delete permission before each run"
                  checked={formData.checkDeleteProbe !== false}
                  onChange={(checked) => setFormData(prev => ({ ...prev, checkDeleteProbe: checked }))}
                >
                  Before each run, FileSync creates and deletes a tiny temporary file in each SFTP folder to prove it is allowed to delete there, and leaves folders alone where it is not.
                  {' '}Turn this off if you don't want a temporary file appearing on {targetName}; a delete the server refuses is then only logged.
                </OptionRow>
              </div>
            )}

            <OptionRow
              id="chk-verify-destination"
              title="Re-send files that go missing from target"
              checked={formData.verifyDestination ?? !isUpload}
              onChange={(checked) => setFormData(prev => ({ ...prev, verifyDestination: checked }))}
            >
              If a file FileSync already delivered is still in {sourceName} but is no longer in {targetName}, FileSync sends it again.
              {' '}Turn this off when {targetName} takes files away after receiving them (Aetna, partner drop-boxes), or the same file is sent again on every run.
            </OptionRow>

            {isUpload && (
              <OptionRow
                id="chk-existing-folders-only"
                title="Only use folders that already exist on target"
                checked={Boolean(formData.existingFoldersOnly)}
                onChange={(checked) => setFormData(prev => ({ ...prev, existingFoldersOnly: checked, enableMirrorPurge: checked ? false : prev.enableMirrorPurge }))}
              >
                FileSync never creates folders on {targetName}. A file goes into its matching subfolder when that folder already exists there; if the folder only exists in ShareFile, the file goes into {formData.sftpRemotePath || 'the target folder'} itself.
                {' '}Use this for servers that don't allow creating folders, like SCAN. If two files would end up with the same name in the same place, neither is sent and the run reports them.
              </OptionRow>
            )}
          </div>

          {/* Modal Footer */}
          <div style={{ borderTop: '1px solid var(--card-border)', paddingTop: '16px', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <div>
              {isEditing && (
                <button
                  type="button"
                  id="btn-delete-transfer-job"
                  className="btn btn-sm"
                  style={{
                    background: 'rgba(239, 68, 68, 0.12)',
                    color: '#ef4444',
                    border: '1px solid rgba(239, 68, 68, 0.3)',
                    fontWeight: 600,
                    display: 'inline-flex',
                    alignItems: 'center',
                    gap: '6px'
                  }}
                  onClick={handleDelete}
                  disabled={isSaving || isTesting}
                >
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                    <polyline points="3 6 5 6 21 6"></polyline>
                    <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path>
                  </svg>
                  {deleteArmed ? 'Click again to delete' : 'Delete Job'}
                </button>
              )}
            </div>
            <div style={{ display: 'flex', gap: '10px' }}>
              <button
                type="button"
                className="btn btn-secondary"
                onClick={onClose}
                disabled={isSaving || isTesting}
              >
                Cancel
              </button>
              <button
                type="submit"
                id="btn-save-transfer-job"
                className="btn btn-primary"
                disabled={isSaving || isTesting}
              >
                {isSaving ? 'Verifying & Saving...' : (isEditing ? 'Save Changes' : 'Create Transfer Job')}
              </button>
            </div>
          </div>
        </form>
      </div>
    </div>
  );
}

// One on/off setting with a bold title and a plain-language explanation underneath
function OptionRow({ id, title, checked, disabled = false, onChange, children }) {
  return (
    <div style={{ marginTop: '10px', padding: '10px 14px', background: 'var(--gray-bg)', borderRadius: '8px', border: '1px solid var(--card-border)', opacity: disabled ? 0.6 : 1 }}>
      <label htmlFor={id} style={{ display: 'flex', alignItems: 'center', gap: '10px', cursor: disabled ? 'not-allowed' : 'pointer', fontSize: '0.85rem', fontWeight: 600, color: 'var(--text-main)', margin: 0 }}>
        <input
          type="checkbox"
          id={id}
          checked={Boolean(checked)}
          disabled={disabled}
          onChange={(e) => onChange(e.target.checked)}
          style={{ width: '16px', height: '16px', cursor: disabled ? 'not-allowed' : 'pointer', accentColor: 'var(--primary-blue)' }}
        />
        <span>{title}</span>
      </label>
      <div style={{ fontSize: '0.74rem', color: 'var(--text-muted)', marginTop: '4px', marginLeft: '26px', lineHeight: 1.45 }}>
        {children}
      </div>
    </div>
  );
}
