﻿using Prism.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using Zametek.Common.Project;
using Zametek.Common.ProjectPlan;
using Zametek.Contract.ProjectPlan;
using Zametek.Maths.Graphs;

namespace Zametek.Client.ProjectPlan.Wpf
{
    public class MetricsManagerViewModel
        : PropertyChangedPubSubViewModel, IMetricsManagerViewModel
    {
        #region Fields

        private readonly object m_Lock;

        private MetricsDto m_MetricsDto;
        private double? m_CriticalityRisk;
        private double? m_FibonacciRisk;
        private double? m_ActivityRisk;
        private double? m_ActivityRiskWithStdDevCorrection;
        private double? m_GeometricCriticalityRisk;
        private double? m_GeometricFibonacciRisk;
        private double? m_GeometricActivityRisk;
        private int? m_CyclomaticComplexity;
        private double? m_DurationManMonths;

        private bool m_IsBusy;

        private readonly ICoreViewModel m_CoreViewModel;
        private readonly IProjectManager m_ProjectManager;
        private readonly IDateTimeCalculator m_DateTimeCalculator;
        private readonly IEventAggregator m_EventService;

        private SubscriptionToken m_GraphCompilationUpdatedPayloadToken;

        #endregion

        #region Ctors

        public MetricsManagerViewModel(
            ICoreViewModel coreViewModel,
            IProjectManager projectManager,
            IDateTimeCalculator dateTimeCalculator,
            IEventAggregator eventService)
            : base(eventService)
        {
            m_Lock = new object();
            m_CoreViewModel = coreViewModel ?? throw new ArgumentNullException(nameof(coreViewModel));
            m_ProjectManager = projectManager ?? throw new ArgumentNullException(nameof(projectManager));
            m_DateTimeCalculator = dateTimeCalculator ?? throw new ArgumentNullException(nameof(dateTimeCalculator));
            m_EventService = eventService ?? throw new ArgumentNullException(nameof(eventService));

            SubscribeToEvents();

            SubscribePropertyChanged(m_CoreViewModel, nameof(m_CoreViewModel.HasCompilationErrors), nameof(HasCompilationErrors), ThreadOption.BackgroundThread);
            SubscribePropertyChanged(m_CoreViewModel, nameof(m_CoreViewModel.HasStaleOutputs), nameof(HasStaleOutputs), ThreadOption.BackgroundThread);
            SubscribePropertyChanged(m_CoreViewModel, nameof(m_CoreViewModel.DirectCost), nameof(DirectCost), ThreadOption.BackgroundThread);
            SubscribePropertyChanged(m_CoreViewModel, nameof(m_CoreViewModel.IndirectCost), nameof(IndirectCost), ThreadOption.BackgroundThread);
            SubscribePropertyChanged(m_CoreViewModel, nameof(m_CoreViewModel.OtherCost), nameof(OtherCost), ThreadOption.BackgroundThread);
            SubscribePropertyChanged(m_CoreViewModel, nameof(m_CoreViewModel.TotalCost), nameof(TotalCost), ThreadOption.BackgroundThread);
        }

        #endregion

        #region Properties

        private bool UseBusinessDays => m_CoreViewModel.UseBusinessDays;

        private GraphCompilation<int, IDependentActivity<int>> GraphCompilation => m_CoreViewModel.GraphCompilation;

        private ArrowGraphSettingsDto ArrowGraphSettingsDto => m_CoreViewModel.ArrowGraphSettingsDto;

        #endregion

        #region Private Methods

        private void SubscribeToEvents()
        {
            m_GraphCompilationUpdatedPayloadToken =
                m_EventService.GetEvent<PubSubEvent<GraphCompilationUpdatedPayload>>()
                    .Subscribe(payload =>
                    {
                        IsBusy = true;
                        CalculateRiskMetrics();
                        CalculateGraphMetrics();
                        IsBusy = false;
                    }, ThreadOption.BackgroundThread);
        }

        private void UnsubscribeFromEvents()
        {
            m_EventService.GetEvent<PubSubEvent<GraphCompilationUpdatedPayload>>()
                .Unsubscribe(m_GraphCompilationUpdatedPayloadToken);
        }

        private void CalculateRiskMetrics()
        {
            lock (m_Lock)
            {
                ClearRiskMetrics();
                IList<IDependentActivity<int>> dependentActivities = GraphCompilation?.DependentActivities;
                if (dependentActivities != null
                    && dependentActivities.Any())
                {
                    if (HasCompilationErrors)
                    {
                        return;
                    }
                    m_MetricsDto = m_ProjectManager.CalculateProjectMetrics(
                        dependentActivities.Where(x => !x.IsDummy).Select(x => (IActivity<int>)x).ToList(),
                        ArrowGraphSettingsDto?.ActivitySeverities);
                    SetRiskMetrics();
                }
            }
        }

        private void ClearRiskMetrics()
        {
            lock (m_Lock)
            {
                CriticalityRisk = null;
                FibonacciRisk = null;
                ActivityRisk = null;
                ActivityRiskWithStdDevCorrection = null;
                GeometricCriticalityRisk = null;
                GeometricFibonacciRisk = null;
                GeometricActivityRisk = null;
            }
        }

        private void SetRiskMetrics()
        {
            lock (m_Lock)
            {
                ClearRiskMetrics();
                MetricsDto metricsDto = m_MetricsDto;
                if (metricsDto != null)
                {
                    CriticalityRisk = metricsDto.Criticality;
                    FibonacciRisk = metricsDto.Fibonacci;
                    ActivityRisk = metricsDto.Activity;
                    ActivityRiskWithStdDevCorrection = metricsDto.ActivityStdDevCorrection;
                    GeometricCriticalityRisk = metricsDto.GeometricCriticality;
                    GeometricFibonacciRisk = metricsDto.GeometricFibonacci;
                    GeometricActivityRisk = metricsDto.GeometricActivity;
                }
            }
        }

        private void CalculateGraphMetrics()
        {
            lock (m_Lock)
            {
                ClearGraphMetrics();
                if (HasCompilationErrors)
                {
                    return;
                }
                SetGraphMetrics();
            }
        }

        private void ClearGraphMetrics()
        {
            lock (m_Lock)
            {
                CyclomaticComplexity = null;
                DurationManMonths = null;
            }
        }

        private void SetGraphMetrics()
        {
            lock (m_Lock)
            {
                ClearGraphMetrics();
                CyclomaticComplexity = m_CoreViewModel.CyclomaticComplexity;
                DurationManMonths = CalculateDurationManMonths();
            }
        }

        private double? CalculateDurationManMonths()
        {
            lock (m_Lock)
            {
                int? durationManDays = m_CoreViewModel.Duration;
                if (!durationManDays.HasValue)
                {
                    return null;
                }
                m_DateTimeCalculator.UseBusinessDays(UseBusinessDays);
                int daysPerWeek = m_DateTimeCalculator.DaysPerWeek;
                return durationManDays.GetValueOrDefault() / (daysPerWeek * 52.0 / 12.0);
            }
        }

        #endregion

        #region IMetricsManagerViewModel Members

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

        public bool HasCompilationErrors => m_CoreViewModel.HasCompilationErrors;

        public bool HasStaleOutputs => m_CoreViewModel.HasStaleOutputs;

        public double? CriticalityRisk
        {
            get
            {
                return m_CriticalityRisk;
            }
            private set
            {
                m_CriticalityRisk = value;
                RaisePropertyChanged();
            }
        }

        public double? FibonacciRisk
        {
            get
            {
                return m_FibonacciRisk;
            }
            private set
            {
                m_FibonacciRisk = value;
                RaisePropertyChanged();
            }
        }

        public double? ActivityRisk
        {
            get
            {
                return m_ActivityRisk;
            }
            private set
            {
                m_ActivityRisk = value;
                RaisePropertyChanged();
            }
        }

        public double? ActivityRiskWithStdDevCorrection
        {
            get
            {
                return m_ActivityRiskWithStdDevCorrection;
            }
            private set
            {
                m_ActivityRiskWithStdDevCorrection = value;
                RaisePropertyChanged();
            }
        }

        public double? GeometricCriticalityRisk
        {
            get
            {
                return m_GeometricCriticalityRisk;
            }
            private set
            {
                m_GeometricCriticalityRisk = value;
                RaisePropertyChanged();
            }
        }

        public double? GeometricFibonacciRisk
        {
            get
            {
                return m_GeometricFibonacciRisk;
            }
            private set
            {
                m_GeometricFibonacciRisk = value;
                RaisePropertyChanged();
            }
        }

        public double? GeometricActivityRisk
        {
            get
            {
                return m_GeometricActivityRisk;
            }
            private set
            {
                m_GeometricActivityRisk = value;
                RaisePropertyChanged();
            }
        }

        public int? CyclomaticComplexity
        {
            get
            {
                return m_CyclomaticComplexity;
            }
            private set
            {
                m_CyclomaticComplexity = value;
                RaisePropertyChanged();
            }
        }

        public double? DurationManMonths
        {
            get
            {
                return m_DurationManMonths;
            }
            private set
            {
                m_DurationManMonths = value;
                RaisePropertyChanged();
            }
        }

        public double? DirectCost => m_CoreViewModel.DirectCost;

        public double? IndirectCost => m_CoreViewModel.IndirectCost;

        public double? OtherCost => m_CoreViewModel.OtherCost;

        public double? TotalCost => m_CoreViewModel.TotalCost;

        #endregion
    }
}
