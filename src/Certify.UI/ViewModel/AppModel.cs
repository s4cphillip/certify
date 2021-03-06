﻿using Certify.Management;
using Certify.Models;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Certify.UI.ViewModel
{
    public class AppModel : BindableBase
    {
        /// <summary>
        /// Provide single static instance of model for all consumers
        /// </summary>
        public static AppModel AppViewModel = new AppModel();

        public const int ProductTypeId = 1;

        private CertifyManager certifyManager = null;

        public PluginManager PluginManager { get; set; }

        #region properties

        /// <summary>
        /// List of all the sites we currently manage
        /// </summary>
        public ObservableCollection<Certify.Models.ManagedSite> ManagedSites { get; set; }

        /// <summary>
        /// If set, there are one or more vault items available to be imported as managed sites
        /// </summary>
        public ObservableCollection<Certify.Models.ManagedSite> ImportedManagedSites { get; set; }

        internal void LoadVaultTree()
        {
            List<VaultItem> tree = new List<VaultItem>();

            // populate registrations
            var registration = new VaultItem { Name = "Registrations" };
            registration.Children = new List<VaultItem>();

            var reg = certifyManager.GetRegistrations();
            foreach (var r in reg)
            {
                r.ItemType = "registration";
                registration.Children.Add(r);
            }

            this.PrimaryContactEmail = registration.Children.FirstOrDefault()?.Name;

            tree.Add(registration);

            // populate identifiers
            var identifiers = new VaultItem { Name = "Identifiers" };
            identifiers.Children = new List<VaultItem>();

            var ids = certifyManager.GetIdentifiers();
            foreach (var i in ids)
            {
                i.ItemType = "identifier";
                identifiers.Children.Add(i);
            }

            tree.Add(identifiers);

            // populate identifiers
            var certs = new VaultItem { Name = "Certificates" };
            certs.Children = new List<VaultItem>();

            var certlist = certifyManager.GetCertificates();
            foreach (var i in ids)
            {
                i.ItemType = "certificate";
                certs.Children.Add(i);
            }

            tree.Add(certs);

            VaultTree = tree;

            this.ACMESummary = certifyManager.GetAcmeSummary();
            this.VaultSummary = certifyManager.GetVaultSummary();

            RaisePropertyChanged(nameof(VaultTree));
        }

        /// <summary>
        /// If true, import from vault/iis scan will merge multi domain sites into one managed site
        /// </summary>
        public bool IsImportSANMergeMode { get; set; }

        public bool HasRegisteredContacts
        {
            get
            {
                return certifyManager.HasRegisteredContacts();
            }
        }

        public bool HasSelectedItemDomainOptions
        {
            get
            {
                if (SelectedItem != null && SelectedItem.DomainOptions != null && SelectedItem.DomainOptions.Any())
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        public Certify.Models.ManagedSite SelectedItem { get; set; }

        public bool IsRegisteredVersion { get; set; }

        public bool SelectedItemHasChanges
        {
            get
            {
                if (this.SelectedItem != null)
                {
                    if (this.SelectedItem.IsChanged || (this.SelectedItem.RequestConfig != null && this.SelectedItem.RequestConfig.IsChanged) || (this.SelectedItem.DomainOptions != null && this.SelectedItem.DomainOptions.Any(d => d.IsChanged)))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public List<SiteBindingItem> WebSiteList
        {
            get
            {
                //get list of sites from IIS
                var iisManager = new IISManager();
                return iisManager.GetPrimarySites(Certify.Properties.Settings.Default.IgnoreStoppedSites);
            }
        }

        internal void SaveManagedItemChanges()
        {
            SelectedItem = GetUpdatedManagedSiteSettings();
            AddOrUpdateManagedSite(SelectedItem);

            MarkAllChangesCompleted();

            RaisePropertyChanged(nameof(IsSelectedItemValid));
        }

        internal void AddContactRegistration(ContactRegistration reg)
        {
            if (certifyManager.AddRegisteredContact(reg))
            {
                //if we now have more than one contact, remove the old one
                certifyManager.RemoveExtraContacts(reg.EmailAddress);

                //refresh content from vault
                LoadVaultTree();
            }
            RaisePropertyChanged(nameof(HasRegisteredContacts));
        }

        public List<IPAddress> HostIPAddresses
        {
            get
            {
                try
                {
                    //return list of ipv4 network IPs
                    IPHostEntry hostEntry = Dns.GetHostEntry(Dns.GetHostName());
                    return hostEntry.AddressList.Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).ToList();
                }
                catch (Exception)
                {
                    //return empty list
                    return new List<IPAddress>();
                }
            }
        }

        /// <summary>
        /// Reset all IsChanged flags for the Selected Item
        /// </summary>
        internal void MarkAllChangesCompleted()
        {
            if (SelectedItem != null)
            {
                //mark all SelectedItem child items and main model as unchanged
                foreach (var opt in SelectedItem.DomainOptions)
                {
                    opt.IsChanged = false;
                }

                SelectedItem.RequestConfig.IsChanged = false;
                SelectedItem.IsChanged = false;
            }

            RaisePropertyChanged(nameof(SelectedItemHasChanges));
        }

        internal void SelectFirstOrDefaultItem()
        {
            SelectedItem = ManagedSites.FirstOrDefault();
        }

        public SiteBindingItem SelectedWebSite
        {
            get; set;
        }

        public DomainOption PrimarySubjectDomain
        {
            get
            {
                if (SelectedItem != null)
                {
                    var primary = SelectedItem.DomainOptions.FirstOrDefault(d => d.IsPrimaryDomain == true);
                    if (primary != null)
                    {
                        return primary;
                    }
                }

                return null;
            }

            set
            {
                foreach (var d in SelectedItem.DomainOptions)
                {
                    if (d.Domain == value.Domain)
                    {
                        d.IsPrimaryDomain = true;
                        d.IsSelected = true;
                    }
                    else
                    {
                        d.IsPrimaryDomain = false;
                    }
                }

                SelectedItem.IsChanged = true;
            }
        }

        /// <summary>
        /// Determine if user should be able to choose/change the Website for the current SelectedItem
        /// </summary>
        public bool IsWebsiteSelectable
        {
            get
            {
                if (SelectedItem != null && SelectedItem.Id == null)
                {
                    return true;
                }
                return false;
            }
        }

        public bool IsItemSelected
        {
            get
            {
                return (this.SelectedItem != null);
            }
        }

        public bool IsNoItemSelected
        {
            get
            {
                return (this.SelectedItem == null);
            }
        }

        public bool IsSelectedItemValid
        {
            get
            {
                if (this.SelectedItem != null && this.SelectedItem.Id != null && this.SelectedItem.IsChanged == false)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        public string ValidationError { get; set; }

        public int MainUITabIndex { get; set; }

        [DependsOn(nameof(ProgressResults))]
        public bool HasRequestsInProgress
        {
            get
            {
                return (ProgressResults != null && ProgressResults.Any());
            }
        }

        public ObservableCollection<RequestProgressState> ProgressResults { get; set; }

        public List<VaultItem> VaultTree { get; set; }

        [DependsOn(nameof(VaultTree))]
        public string ACMESummary { get; set; }

        [DependsOn(nameof(VaultTree))]
        public string VaultSummary { get; set; }

        public string PrimaryContactEmail { get; set; }

        public bool IsUpdateAvailable { get; set; }

        /// <summary>
        /// If an update is available this will contain more info about the new update
        /// </summary>
        public UpdateCheck UpdateCheckResult { get; set; }

        #endregion properties

        #region methods

        public AppModel()
        {
            certifyManager = new CertifyManager();

            ProgressResults = new ObservableCollection<RequestProgressState>();
        }

        public void PreviewImport(bool sanMergeMode)
        {
            AppViewModel.IsImportSANMergeMode = sanMergeMode;
            //we have no managed sites, offer to import them from vault if we have one
            var importedSites = certifyManager.ImportManagedSitesFromVault(sanMergeMode);
            ImportedManagedSites = new ObservableCollection<ManagedSite>(importedSites);
        }

        public void LoadSettings()
        {
            this.ManagedSites = new ObservableCollection<ManagedSite>(certifyManager.GetManagedSites());
            this.ImportedManagedSites = new ObservableCollection<ManagedSite>();

            if (!this.ManagedSites.Any())
            {
                //if we have a vault, preview import. //TODO: make this async and only perform after UI has shown
                PreviewImport(sanMergeMode: true);

                //if we have no vault, start a new one

                //if we have no registered contacts in the vault then prompt to register a contact
            }
            /*if (this.ManagedSites.Any())
            {
                //preselect the first managed site
                //  this.SelectedItem = this.ManagedSites[0];

                //test state
                BeginTrackingProgress(new RequestProgressState { CurrentState = RequestState.InProgress, IsStarted = true, Message = "Registering Domain Identifier", ManagedItem = ManagedSites[0] });
                BeginTrackingProgress(new RequestProgressState { CurrentState = RequestState.Error, IsStarted = true, Message = "Rate Limited", ManagedItem = ManagedSites[0] });
            }*/
        }

        public void SaveSettings(object param)
        {
            certifyManager.SaveManagedSites(this.ManagedSites.ToList());
        }

        public async void RenewAll(bool autoRenewalsOnly)
        {
            Dictionary<string, Progress<RequestProgressState>> itemTrackers = new Dictionary<string, Progress<RequestProgressState>>();
            foreach (var s in ManagedSites)
            {
                if ((autoRenewalsOnly && s.IncludeInAutoRenew) || !autoRenewalsOnly)
                {
                    var progressState = new RequestProgressState { ManagedItem = s };
                    itemTrackers.Add(s.Id, new Progress<RequestProgressState>(progressState.ProgressReport));

                    //begin monitoring progress
                    BeginTrackingProgress(progressState);
                }
            }

            var results = await certifyManager.PerformRenewalAllManagedSites(autoRenewalsOnly, itemTrackers);
            //TODO: store results in log
            //return results;
        }

        public ManagedItem AddOrUpdateManagedSite(ManagedSite item)
        {
            var existing = this.ManagedSites.FirstOrDefault(s => s.Id == item.Id);

            //add new or replace existing

            if (existing != null)
            {
                this.ManagedSites.Remove(existing);
            }

            this.ManagedSites.Add(item);

            //save settings
            certifyManager.SaveManagedSites(this.ManagedSites.ToList());

            return item;
        }

        internal void DeleteManagedSite(ManagedSite selectedItem)
        {
            var existing = this.ManagedSites.FirstOrDefault(s => s.Id == selectedItem.Id);

            //remove existing

            if (existing != null)
            {
                this.ManagedSites.Remove(existing);
            }

            //save settings
            certifyManager.SaveManagedSites(this.ManagedSites.ToList());
        }

        public void SANSelectAll(object o)
        {
            if (this.SelectedItem != null && this.SelectedItem.DomainOptions != null)
            {
                foreach (var opt in this.SelectedItem.DomainOptions)
                {
                    opt.IsSelected = true;
                }
            }
        }

        public void SANSelectNone(object o)
        {
            if (this.SelectedItem != null && this.SelectedItem.DomainOptions != null)
            {
                foreach (var opt in this.SelectedItem.DomainOptions)
                {
                    opt.IsSelected = false;
                }

                // RaisePropertyChanged(nameof(SelectedItem));
            }
        }

        /// <summary>
        /// For the given set of options get a new CertRequestConfig to store
        /// </summary>
        /// <returns></returns>
        private ManagedSite GetUpdatedManagedSiteSettings()
        {
            var item = SelectedItem;
            // item.DomainOptions = new ObservableCollection<DomainOption>();
            var config = item.RequestConfig;

            // RefreshDomainOptionSettingsFromUI();
            var primaryDomain = item.DomainOptions.FirstOrDefault(d => d.IsPrimaryDomain == true);

            //if no primary domain need to go back and select one
            if (primaryDomain == null) throw new ArgumentException("Primary subject domain must be set.");

            var _idnMapping = new System.Globalization.IdnMapping();
            config.PrimaryDomain = _idnMapping.GetAscii(primaryDomain.Domain); // ACME service requires international domain names in ascii mode

            //apply remaining selected domains as subject alternative names
            config.SubjectAlternativeNames =
                item.DomainOptions.Where(dm => dm.IsSelected == true)
                .Select(i => i.Domain)
                .ToArray();

            //config.PerformChallengeFileCopy = true;
            //config.PerformExtensionlessConfigChecks = !chkSkipConfigCheck.Checked;
            config.PerformAutoConfig = true;

            // config.EnableFailureNotifications = chkEnableNotifications.Checked;

            //determine if this site has an existing entry in Managed Sites, if so use that, otherwise start a new one

            if (SelectedItem.Id == null)
            {
                var siteInfo = SelectedWebSite;
                //if siteInfo null we need to go back and select a site

                item.Id = Guid.NewGuid().ToString() + ":" + siteInfo.SiteId;
                item.GroupId = siteInfo.SiteId;

                config.WebsiteRootPath = Environment.ExpandEnvironmentVariables(siteInfo.PhysicalPath);
            }

            item.ItemType = ManagedItemType.SSL_LetsEncrypt_LocalIIS;

            //store domain options settings and request config for this site so we can replay for automated renewal

            //managedSite.RequestConfig = config;

            return item;
        }

        private void PopulateManagedSiteSettings(string siteId)
        {
            ValidationError = null;
            var managedSite = SelectedItem;
            managedSite.Name = SelectedWebSite.SiteName;

            //TODO: if this site would be a duplicate need to increment the site name

            //set defaults first
            managedSite.RequestConfig.PerformExtensionlessConfigChecks = true;
            managedSite.RequestConfig.PerformChallengeFileCopy = true;
            managedSite.RequestConfig.PerformAutomatedCertBinding = true;
            managedSite.RequestConfig.PerformAutoConfig = true;
            managedSite.RequestConfig.EnableFailureNotifications = true;
            managedSite.RequestConfig.ChallengeType = "http-01";
            managedSite.IncludeInAutoRenew = true;
            managedSite.ClearDomainOptions();
            //for the given selected web site, allow the user to choose which domains to combine into one certificate

            List<DomainOption> domainOptions = certifyManager.GetDomainOptionsFromSite(siteId);
            if (domainOptions.Any())
            {
                managedSite.AddDomainOptions(domainOptions);
            }

            if (!managedSite.DomainOptions.Any())
            {
                ValidationError = "The selected site has no domain bindings setup. Configure the domains first using Edit Bindings in IIS.";
            }

            //TODO: load settings from previously saved managed site?
            RaisePropertyChanged(nameof(PrimarySubjectDomain));

            RaisePropertyChanged(nameof(HasSelectedItemDomainOptions));
        }

        public async void BeginCertificateRequest(string managedItemId)
        {
            //begin request process
            var managedSite = ManagedSites.FirstOrDefault(s => s.Id == managedItemId);

            if (managedSite != null)
            {
                MainUITabIndex = (int)MainWindow.PrimaryUITabs.CurrentProgress;

                //add request to observable list of progress state
                RequestProgressState progressState = new RequestProgressState();
                progressState.ManagedItem = managedSite;

                //begin monitoring progress
                BeginTrackingProgress(progressState);

                var progressIndicator = new Progress<RequestProgressState>(progressState.ProgressReport);
                var result = await certifyManager.PerformCertificateRequest(null, managedSite, progressIndicator);

                if (progressIndicator != null)
                {
                    var progress = (IProgress<RequestProgressState>)progressIndicator;

                    if (result.IsSuccess)
                    {
                        progress.Report(new RequestProgressState { CurrentState = RequestState.Success, Message = result.Message });
                    }
                    else
                    {
                        progress.Report(new RequestProgressState { CurrentState = RequestState.Error, Message = result.Message });
                    }
                }
            }
        }

        private void BeginTrackingProgress(RequestProgressState state)
        {
            var existing = ProgressResults.FirstOrDefault(p => p.ManagedItem.Id == state.ManagedItem.Id);
            if (existing != null)
            {
                ProgressResults.Remove(existing);
            }
            ProgressResults.Add(state);

            RaisePropertyChanged(nameof(HasRequestsInProgress));
        }

        #endregion methods

        #region commands

        public ICommand SANSelectAllCommand => new RelayCommand<object>(SANSelectAll);
        public ICommand SANSelectNoneCommand => new RelayCommand<object>(SANSelectNone);

        public ICommand AddContactCommand => new RelayCommand<ContactRegistration>(AddContactRegistration);

        public ICommand PopulateManagedSiteSettingsCommand => new RelayCommand<string>(PopulateManagedSiteSettings);
        public ICommand BeginCertificateRequestCommand => new RelayCommand<string>(BeginCertificateRequest);
        public ICommand RenewAllCommand => new RelayCommand<bool>(RenewAll);

        #endregion commands
    }
}