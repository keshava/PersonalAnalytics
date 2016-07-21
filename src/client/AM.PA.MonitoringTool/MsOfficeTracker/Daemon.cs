﻿// Created by André Meyer at MSR
// Created: 2015-12-07
// 
// Licensed under the MIT License.

using System;
using Shared;
using System.Collections.Generic;
using System.Windows;
using MsOfficeTracker.Visualizations;
using MsOfficeTracker.Data;
using Shared.Data;
using System.Globalization;
using System.Threading;
using MsOfficeTracker.Helpers;

namespace MsOfficeTracker
{
    public class Daemon : BaseTracker, ITracker
    {
        private Timer _timer;
        private DateTime _lastDayEntry;

        #region ITracker Stuff

        public event EventHandler StatusChanged;

        public Daemon()
        {
            Name = "Office 365 Tracker";
        }

        public async override void Start()
        {
            // on first start, a pop-up is shown to ask the user to enable/disable the tracker
            if (IsOffice365ApiFirstUse())
            {
                var msg = string.Format(CultureInfo.InvariantCulture, "This (updated) version of the {1} tool allows you to collect information about the meetings you attend and the emails you send/receive in a work day. In case you enable this tracker, you will need to authenticate with your Office 365 work account.\n\nThe contents of the emails and meetings are NOT accessed. You can manually disable or enable this tracker anytime in the settings.\n\nDo you want to enable the {0}?", Name, Dict.ToolName);
                var res = MessageBox.Show(msg,
                    Dict.ToolName + ": " + Name, 
                    MessageBoxButton.YesNo);
                if (res == MessageBoxResult.Yes)
                {
                    _msOfficeTrackerEnabled = true;
                    Database.GetInstance().SetSettings("MsOfficeTrackerEnabled", true);
                }
                else
                {
                    IsRunning = false;
                    OnStatusChanged(new EventArgs());
                    MsOfficeTrackerEnabled = false;
                    return; // don't start the tracker !
                }
            }

            // initialize API & authenticate if necessary
            var isAuthenticated = await Office365Api.GetInstance().Authenticate();

            // disable tracker if authentication was without success
            if (!isAuthenticated)
            {
                IsRunning = false;
                OnStatusChanged(new EventArgs());

                var msg = string.Format(CultureInfo.InvariantCulture, "The {0} was disabled as the authentication with Office 365 failed. Maybe you don't have an internet connection or the Office 365 credentials were wrong.\n\nThe tool will prompt the Office 365 login again with the next start of the application. You can also disable the {0} in the settings.\n\nIf the problem persists, please contact us via t-anmeye@microsoft.com and attach the logfile.", Name);
                MessageBox.Show(msg, Dict.ToolName + ": Error", MessageBoxButton.OK); //todo: use toast message
            }
            else
            {
                IsRunning = true;
                OnStatusChanged(new EventArgs());
            }

            // Start Email Count Timer
            if (_timer != null)
                Stop();

            // initialize a new timer
            var interval = (int)TimeSpan.FromMinutes(Settings.SaveEmailCountsIntervalInMinutes).TotalMilliseconds;
            _timer = new Timer(new TimerCallback(TimerTick), // callback
                            null,  // no idea
                            10000, // start immediately after 10 seconds
                            interval); // interval
        }

        public override void Stop()
        {
            if (_timer != null)
            {
                _timer.Dispose();
                _timer = null;
            }

            IsRunning = false;
            OnStatusChanged(new EventArgs());
        }

        protected virtual void OnStatusChanged(EventArgs e)
        {
            if (StatusChanged != null)
                StatusChanged(this, e);
        }

        public override void CreateDatabaseTablesIfNotExist()
        {
            Queries.CreateMsTrackerTables();
        }

        /// <summary>
        /// Checks if the office API is used for the first time
        /// </summary>
        /// <returns>true if there is no setting stored, else otherwise</returns>
        private bool IsOffice365ApiFirstUse ()
        {
            return ! Database.GetInstance().HasSetting("MsOfficeTrackerEnabled");
        }

        public override List<IVisualization> GetVisualizationsDay(DateTimeOffset date)
        {
            var vis1 = new DayEmailsReceivedAndSent(date);
            var vis2 = new DayMeetingsAttended(date);
            var vis3 = new DayChatsReceivedAndSent(date);
            var vis4 = new DayCallsReceivedAndSent(date);
            return new List<IVisualization> { vis1, vis2, vis3, vis4 };
        }

        public override bool IsEnabled()
        {
            return MsOfficeTrackerEnabled;
        }

        private bool _msOfficeTrackerEnabled;
        public bool MsOfficeTrackerEnabled
        {
            get
            {
                _msOfficeTrackerEnabled = Database.GetInstance().GetSettingsBool("MsOfficeTrackerEnabled", Settings.IsEnabledByDefault);
                return _msOfficeTrackerEnabled;
            }
            set
            {
                var updatedIsEnabled = value;

                // only update if settings changed
                if (updatedIsEnabled == _msOfficeTrackerEnabled) return;

                // update settings
                Database.GetInstance().SetSettings("MsOfficeTrackerEnabled", updatedIsEnabled);

                // start/stop tracker if necessary
                if (!updatedIsEnabled && IsRunning)
                {
                    Stop();
                }
                else if (updatedIsEnabled && !IsRunning)
                {
                    Start();
                }

                // if tracker was disabled out, sign out and clear cache
                if (updatedIsEnabled == false)
                {
                    Office365Api.GetInstance().SignOut();
                }

                // log
                Database.GetInstance().LogInfo("The participant updated the setting 'MsOfficeTrackerEnabled' to " + updatedIsEnabled);
            }
        }

        #endregion

        #region Deamon

        private void TimerTick(object state)
        {
            var now = DateTime.Now;

            // save email infos & meetings infos for current timestamp
            SaveEmailsCount(now);
            SaveMeetingsCount(now);
            SaveChatsAndCallsCount(now);

            // go a few days back to cache email & meeting data (if necessary)
            SaveDaysBeforeCounts();
        }

        /// <summary>
        /// Regularly runs and saves some email counts
        /// </summary>
        private void SaveEmailsCount(DateTime date)
        {
            try
            {
                // don't do it if already done for the date; always check for the current date
                if (date.Date != DateTime.Now.Date && Queries.HasEmailsEntriesForDate(date, true)) return;

                // create and save a new email snapshot (inbox, sent, received)
                Queries.CreateEmailsSnapshot(date, true);
                    
            }
            catch (Exception e)
            {
                Logger.WriteToLogFile(e);
            }
        }

        /// <summary>
        /// Regularly runs and saves some email counts
        /// </summary>
        private void SaveMeetingsCount(DateTimeOffset date)
        {
            try
            {
                // don't do it if already done for the date; always check for the current date
                if (date.Date != DateTime.Now.Date // always do it for current day
                    && Queries.HasMeetingEntriesForDate(date.Date)) // only do it for past day, if there are no entries
                    return;

                // get meetings for the date
                var meetingsResult = Office365Api.GetInstance().LoadMeetings(date);
                meetingsResult.Wait();
                var meetings = meetingsResult.Result;

                if (meetings.Count > 0)
                {
                    // delete old entries (to add updated meetings)
                    Queries.RemoveMeetingsForDate(date);

                    // save new meetings into the database
                    foreach (var meeting in meetings)
                    {
                        var duration = (int)Math.Round(Math.Abs((meeting.End - meeting.Start).TotalMinutes), 0);
                        if (duration >= 24 * 60) continue; // only store if not multiple-day meeting
                        var start = meeting.Start.ToLocalTime();
                        Queries.SaveMeetingsSnapshot(start, meeting.Subject, duration);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.WriteToLogFile(e);
            }
        }

        /// <summary>
        /// Regularly runs and saves some chats and calls counts
        /// </summary>
        private void SaveChatsAndCallsCount(DateTime date)
        {
            try
            {
                // don't do it if already done for the date; always check for the current date
                if (date.Date != DateTime.Now.Date && Queries.HasChatsEntriesForDate(date, true) && Queries.HasCallsEntriesForDate(date, true)) return;

                // create and save a new email snapshot (inbox, sent, received)
                Queries.CreateChatsAndCallsSnapshot(date, true);

            }
            catch (Exception e)
            {
                Logger.WriteToLogFile(e);
            }
        }

        /// <summary>
        /// Once a day, save yesterday's (and some other days before) meetings and emails
        /// </summary>
        private void SaveDaysBeforeCounts()
        {
            try
            {
                // only save yesterday counts if NOT already done (more efficient to not waste resources!)
                // if it was yesterday, the other days are probably in
                var yesterday = DateTime.Now.AddDays(-1);
                if (_lastDayEntry.Date == yesterday.Date) return;

                // so, now we are good for yesterday and some days before
                _lastDayEntry = yesterday;

                // go 'UpdateCacheForDays' back and update the cache
                for (var day = 1; day <= Settings.UpdateCacheForDays; day++)
                {
                    var date = DateTime.Now.AddDays(-day);
                    SaveEmailsCount(date);
                    SaveMeetingsCount(date);
                    SaveChatsAndCallsCount(date);
                }  
            }
            catch (Exception e)
            {
                Logger.WriteToLogFile(e);
            }
        }

        #endregion
    }
}
