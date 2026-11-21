// The kinds of remote side a job can have (FileSync.Core RemoteEndpointType). Portal URLs are prefilled in the job editor.
export const ENDPOINT_TYPES = {
  Sftp: {
    label: 'SFTP Server',
    shortName: 'SFTP',
    defaultUrl: ''
  },
  AetnaSecureTransport: {
    label: 'Aetna (SecureTransport web client)',
    shortName: 'Aetna',
    defaultUrl: 'https://st1.aetna.com/',
    folderExample: { download: '/StarsMedicare_Reports/fromAetna', upload: '/StarsMedicare_Reports/toAetna' }
  },
  FilesCom: {
    label: 'Champion (Files.com)',
    shortName: 'Champion',
    defaultUrl: 'https://championps.files.com',
    folderExample: {
      download: '/ (whole site)',
      upload: '/IPA_Operations/5063_Guidant Community Alliance Medical Group/To_CPS'
    }
  },
  ScanPortal: {
    label: 'SCAN provider portal (download only)',
    shortName: 'SCAN',
    defaultUrl: 'https://secure-pportal.scanhealthplan.com/v3app/publicservice/loginv1/login.aspx?bc=a7d01ea2-5262-422b-9f54-7951a5a7fe06&serviceid=9b005fb3-f143-4710-8870-e9c91ddf549c',
    downloadOnly: true,
    folderExample: { download: '/ (all groups) or /N01145' }
  }
};

export const isPortal = (endpointType) => Boolean(endpointType) && endpointType !== 'Sftp';

export const remoteShortName = (endpointType) => (ENDPOINT_TYPES[endpointType] || ENDPOINT_TYPES.Sftp).shortName;
