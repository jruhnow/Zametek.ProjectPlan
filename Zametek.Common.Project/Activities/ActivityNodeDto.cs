﻿using System;
using System.Collections.Generic;
using Zametek.Maths.Graphs;

namespace Zametek.Common.Project
{
    [Serializable]
    public class ActivityNodeDto
    {
        public NodeType NodeType { get; set; }
        public ActivityDto Content { get; set; }
        public List<int> IncomingEdges { get; set; }
        public List<int> OutgoingEdges { get; set; }
    }
}
