﻿using Prism.Commands;
using Prism.Events;
using Prism.Interactivity.InteractionRequest;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using Zametek.Common.Project;

namespace Zametek.Client.ProjectPlan.Wpf
{
    public class ActivitiesManagerViewModel
        : PropertyChangedPubSubViewModel, IActivitiesManagerViewModel
    {
        #region Fields

        private readonly object m_Lock;
        private bool m_IsBusy;

        private readonly ICoreViewModel m_CoreViewModel;
        private readonly IEventAggregator m_EventService;

        private readonly InteractionRequest<Notification> m_NotificationInteractionRequest;

        private SubscriptionToken m_ManagedActivityUpdatedPayloadToken;
        private SubscriptionToken m_ProjectStartUpdatedPayloadToken;
        private SubscriptionToken m_UseBusinessDaysUpdatedPayloadToken;
        private SubscriptionToken m_ShowDatesUpdatedPayloadToken;

        #endregion

        #region Ctors

        public ActivitiesManagerViewModel(
            ICoreViewModel coreViewModel,
            IEventAggregator eventService)
            : base(eventService)
        {
            m_Lock = new object();
            m_CoreViewModel = coreViewModel ?? throw new ArgumentNullException(nameof(coreViewModel));
            m_EventService = eventService ?? throw new ArgumentNullException(nameof(eventService));

            SelectedActivities = new ObservableCollection<ManagedActivityViewModel>();

            m_NotificationInteractionRequest = new InteractionRequest<Notification>();

            InitializeCommands();
            SubscribeToEvents();

            SubscribePropertyChanged(m_CoreViewModel, nameof(m_CoreViewModel.HasStaleOutputs), nameof(HasStaleOutputs), ThreadOption.BackgroundThread);
            SubscribePropertyChanged(m_CoreViewModel, nameof(m_CoreViewModel.HasCompilationErrors), nameof(HasCompilationErrors), ThreadOption.BackgroundThread);
            SubscribePropertyChanged(m_CoreViewModel, nameof(m_CoreViewModel.CompilationOutput), nameof(CompilationOutput), ThreadOption.BackgroundThread);
            SubscribePropertyChanged(m_CoreViewModel, nameof(m_CoreViewModel.ShowDates), nameof(ShowDates), ThreadOption.BackgroundThread);
            SubscribePropertyChanged(m_CoreViewModel, nameof(m_CoreViewModel.ShowDates), nameof(ShowDays), ThreadOption.BackgroundThread);
        }

        #endregion

        #region Properties

        private bool IsProjectUpdated
        {
            set
            {
                m_CoreViewModel.IsProjectUpdated = value;
            }
        }

        private bool UseBusinessDays => m_CoreViewModel.UseBusinessDays;

        private DateTime ProjectStart => m_CoreViewModel.ProjectStart;

        #endregion

        #region Commands

        public DelegateCommandBase InternalSetSelectedManagedActivitiesCommand
        {
            get;
            private set;
        }

        private void SetSelectedManagedActivities(SelectionChangedEventArgs args)
        {
            if (args?.AddedItems != null)
            {
                SelectedActivities.AddRange(args?.AddedItems.OfType<ManagedActivityViewModel>());
            }
            if (args?.RemovedItems != null)
            {
                foreach (var managedActivityViewModel in args?.RemovedItems.OfType<ManagedActivityViewModel>())
                {
                    SelectedActivities.Remove(managedActivityViewModel);
                }
            }
            RaisePropertyChanged(nameof(SelectedActivity));
            RaiseCanExecuteChangedAllCommands();
        }

        private DelegateCommandBase InternalAddManagedActivityCommand
        {
            get;
            set;
        }

        private async void AddManagedActivity()
        {
            await DoAddManagedActivityAsync();
        }

        private bool CanAddManagedActivity()
        {
            return true;
        }

        private DelegateCommandBase InternalRemoveManagedActivityCommand
        {
            get;
            set;
        }

        private async void RemoveManagedActivity()
        {
            await DoRemoveManagedActivityAsync();
        }

        private bool CanRemoveManagedActivity()
        {
            return SelectedActivities.Any();
        }

        #endregion

        #region Private Methods

        private void InitializeCommands()
        {
            SetSelectedManagedActivitiesCommand =
                InternalSetSelectedManagedActivitiesCommand =
                    new DelegateCommand<SelectionChangedEventArgs>(SetSelectedManagedActivities);
            AddManagedActivityCommand =
                InternalAddManagedActivityCommand =
                    new DelegateCommand(AddManagedActivity, CanAddManagedActivity);
            RemoveManagedActivityCommand =
                InternalRemoveManagedActivityCommand =
                    new DelegateCommand(RemoveManagedActivity, CanRemoveManagedActivity);
        }

        private void RaiseCanExecuteChangedAllCommands()
        {
            InternalSetSelectedManagedActivitiesCommand.RaiseCanExecuteChanged();
            InternalAddManagedActivityCommand.RaiseCanExecuteChanged();
            InternalRemoveManagedActivityCommand.RaiseCanExecuteChanged();
        }

        private void SubscribeToEvents()
        {
            m_ManagedActivityUpdatedPayloadToken =
                m_EventService.GetEvent<PubSubEvent<ManagedActivityUpdatedPayload>>()
                    .Subscribe(async payload =>
                    {
                        IsProjectUpdated = true;
                        await UpdateActivitiesTargetResourceDependenciesAsync();
                        await DoAutoCompileAsync();
                    }, ThreadOption.BackgroundThread);
            m_ProjectStartUpdatedPayloadToken =
                m_EventService.GetEvent<PubSubEvent<ProjectStartUpdatedPayload>>()
                    .Subscribe(async payload =>
                    {
                        IsProjectUpdated = true;
                        await UpdateActivitiesProjectStartAsync();
                        await DoAutoCompileAsync();
                    }, ThreadOption.BackgroundThread);
            m_UseBusinessDaysUpdatedPayloadToken =
                m_EventService.GetEvent<PubSubEvent<UseBusinessDaysUpdatedPayload>>()
                    .Subscribe(async payload =>
                    {
                        IsProjectUpdated = true;
                        await UpdateActivitiesUseBusinessDaysAsync();
                        await DoAutoCompileAsync();
                    }, ThreadOption.BackgroundThread);
            m_ShowDatesUpdatedPayloadToken =
                m_EventService.GetEvent<PubSubEvent<ShowDatesUpdatedPayload>>()
                    .Subscribe(async payload =>
                    {
                        await SetCompilationOutputAsync();
                        PublishGraphCompilationUpdatedPayload();
                    }, ThreadOption.BackgroundThread);
        }

        private void UnsubscribeFromEvents()
        {
            m_EventService.GetEvent<PubSubEvent<ManagedActivityUpdatedPayload>>()
                .Unsubscribe(m_ManagedActivityUpdatedPayloadToken);
            m_EventService.GetEvent<PubSubEvent<ProjectStartUpdatedPayload>>()
                .Unsubscribe(m_ProjectStartUpdatedPayloadToken);
            m_EventService.GetEvent<PubSubEvent<UseBusinessDaysUpdatedPayload>>()
                .Unsubscribe(m_UseBusinessDaysUpdatedPayloadToken);
            m_EventService.GetEvent<PubSubEvent<ShowDatesUpdatedPayload>>()
                .Unsubscribe(m_ShowDatesUpdatedPayloadToken);
        }

        private void PublishGraphCompilationUpdatedPayload()
        {
            m_EventService.GetEvent<PubSubEvent<GraphCompilationUpdatedPayload>>()
                .Publish(new GraphCompilationUpdatedPayload());
        }

        private async Task UpdateActivitiesTargetResourceDependenciesAsync()
        {
            await Task.Run(() => m_CoreViewModel.UpdateActivitiesTargetResourceDependencies());
        }

        private async Task UpdateActivitiesProjectStartAsync()
        {
            await Task.Run(() => m_CoreViewModel.UpdateActivitiesProjectStart());
        }

        private async Task UpdateActivitiesUseBusinessDaysAsync()
        {
            await Task.Run(() => m_CoreViewModel.UpdateActivitiesUseBusinessDays());
        }

        private async Task RunAutoCompileAsync()
        {
            await Task.Run(() => m_CoreViewModel.RunAutoCompile());
        }

        private async Task SetCompilationOutputAsync()
        {
            await Task.Run(() => m_CoreViewModel.SetCompilationOutput());
        }

        private void DispatchNotification(string title, object content)
        {
            m_NotificationInteractionRequest.Raise(
                new Notification
                {
                    Title = title,
                    Content = content
                });
        }

        #endregion

        #region Public Methods

        public async Task DoAutoCompileAsync()
        {
            try
            {
                IsBusy = true;
                HasStaleOutputs = true;
                IsProjectUpdated = true;
                await RunAutoCompileAsync();
            }
            catch (Exception ex)
            {
                DispatchNotification(
                    Properties.Resources.Title_Error,
                    ex.Message);
            }
            finally
            {
                IsBusy = false;
                RaiseCanExecuteChangedAllCommands();
            }
        }

        public async Task DoAddManagedActivityAsync()
        {
            try
            {
                IsBusy = true;

                lock (m_Lock)
                {
                    m_CoreViewModel.AddManagedActivity();
                }

                HasStaleOutputs = true;
                IsProjectUpdated = true;

                await RunAutoCompileAsync();
            }
            catch (Exception ex)
            {
                DispatchNotification(
                    Properties.Resources.Title_Error,
                    ex.Message);
            }
            finally
            {
                IsBusy = false;
                RaiseCanExecuteChangedAllCommands();
            }
        }

        public async Task DoRemoveManagedActivityAsync()
        {
            try
            {
                IsBusy = true;

                lock (m_Lock)
                {
                    var activityIds = new HashSet<int>(SelectedActivities.Select(x => x.Id));
                    if (!activityIds.Any())
                    {
                        return;
                    }
                    m_CoreViewModel.RemoveManagedActivities(activityIds);
                }

                HasStaleOutputs = true;
                IsProjectUpdated = true;

                await RunAutoCompileAsync();
            }
            catch (Exception ex)
            {
                DispatchNotification(
                    Properties.Resources.Title_Error,
                    ex.Message);
            }
            finally
            {
                SelectedActivities.Clear();
                RaisePropertyChanged(nameof(Activities));
                RaisePropertyChanged(nameof(SelectedActivities));
                IsBusy = false;
                RaiseCanExecuteChangedAllCommands();
            }
        }

        #endregion

        #region IActivityManagerViewModel Members

        public IInteractionRequest NotificationInteractionRequest => m_NotificationInteractionRequest;

        public bool IsBusy
        {
            get
            {
                return m_IsBusy;
            }
            private set
            {
                m_IsBusy = value;
                RaisePropertyChanged();
            }
        }

        public bool HasStaleOutputs
        {
            get
            {
                return m_CoreViewModel.HasStaleOutputs;
            }
            private set
            {
                lock (m_Lock)
                {
                    m_CoreViewModel.HasStaleOutputs = value;
                }
                RaisePropertyChanged();
            }
        }

        public bool ShowDates
        {
            get
            {
                return m_CoreViewModel.ShowDates;
            }
        }

        public bool ShowDays
        {
            get
            {
                return !ShowDates;
            }
        }

        public bool HasCompilationErrors
        {
            get
            {
                return m_CoreViewModel.HasCompilationErrors;
            }
            private set
            {
                lock (m_Lock)
                {
                    m_CoreViewModel.HasCompilationErrors = value;
                }
                RaisePropertyChanged();
            }
        }

        public string CompilationOutput
        {
            get
            {
                return m_CoreViewModel.CompilationOutput;
            }
        }

        public ObservableCollection<ManagedActivityViewModel> Activities => m_CoreViewModel.Activities;

        public ObservableCollection<ManagedActivityViewModel> SelectedActivities
        {
            get;
        }

        public ManagedActivityViewModel SelectedActivity
        {
            get
            {
                if (SelectedActivities.Count == 1)
                {
                    return SelectedActivities.FirstOrDefault();
                }
                return null;
            }
        }

        public ICommand SetSelectedManagedActivitiesCommand
        {
            get;
            private set;
        }

        public ICommand AddManagedActivityCommand
        {
            get;
            private set;
        }

        public ICommand RemoveManagedActivityCommand
        {
            get;
            private set;
        }

        #endregion
    }
}
