﻿using Prism.Interactivity.InteractionRequest;

namespace Zametek.Client.ProjectPlan.Wpf
{
    public class AboutViewModel
        : BasicNotificationViewModel
    {
        public string AppName => Properties.Resources.Label_AppName;

        public string AppVersion => Properties.Resources.Label_AppVersion;

        public string Copyright => Properties.Resources.Label_Copyright;
    }
}
