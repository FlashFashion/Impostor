﻿namespace Impostor.Server.Data
{
    public class DebugConfig
    {
        public const string Section = "Debug";

        public bool GameRecorderEnabled { get; set; }
        public string GameRecorderPath { get; set; }
    }
}