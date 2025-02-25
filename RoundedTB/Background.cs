﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace RoundedTB
{
    public class Background
    {
        // Just have a reference point for the Dispatcher
        public MainWindow mw;
        bool redrawOverride = false;
        int infrequentCount = 0;

        public Background()
        {
            mw = (MainWindow)Application.Current.MainWindow;
        }


        // Main method for the BackgroundWorker - runs indefinitely
        public void DoWork(object sender, DoWorkEventArgs e)
        {
            mw.interaction.AddLog("in bw");
            BackgroundWorker worker = sender as BackgroundWorker;
            while (true)
            {
                try
                {
                    if (worker.CancellationPending == true)
                    {
                        mw.interaction.AddLog("cancelling");
                        e.Cancel = true;
                        break;
                    }

                    // Primary loop for the running process
                    else
                    {
                        // Section for running less important things without requiring an additional thread
                        infrequentCount++;
                        if (infrequentCount == 10)
                        {
                            // Check to see if settings need to be shown
                            List<IntPtr> windowList = Interaction.GetTopLevelWindows();
                            foreach (IntPtr hwnd in windowList)
                            {
                                StringBuilder windowClass = new StringBuilder(1024);
                                StringBuilder windowTitle = new StringBuilder(1024);
                                try
                                {
                                    LocalPInvoke.GetClassName(hwnd, windowClass, 1024);
                                    LocalPInvoke.GetWindowText(hwnd, windowTitle, 1024);

                                    if (windowClass.ToString().Contains("HwndWrapper[RoundedTB.exe") && windowTitle.ToString() == "RoundedTB_SettingsRequest")
                                    {
                                        mw.Dispatcher.Invoke(() =>
                                        {
                                            if (mw.Visibility != Visibility.Visible)
                                            {
                                                mw.ShowMenuItem_Click(null, null);
                                            }
                                        });
                                        LocalPInvoke.SetWindowText(hwnd, "RoundedTB");
                                    }
                                }
                                catch (Exception) { }
                            }

                            // Update tray icon
                            mw.Dispatcher.Invoke(() =>
                            {
                                mw.TrayIconCheck();
                            });

                            infrequentCount = 0;
                        }

                        // Check if the taskbar is centred, and if it is, directly update the settings; using an interim bool to avoid delaying because I'm lazy
                        bool isCentred = Taskbar.CheckIfCentred();
                        mw.activeSettings.IsCentred = isCentred;

                        // Work with static values to avoid some null reference exceptions
                        List<Types.Taskbar> taskbars = mw.taskbarDetails;
                        Types.Settings settings = mw.activeSettings;

                        // If the number of taskbars has changed, regenerate taskbar information
                        if (Taskbar.TaskbarCountOrHandleChanged(taskbars.Count, taskbars[0].TaskbarHwnd))
                        {
                            // Forcefully reset taskbars if the taskbar count or main taskbar handle has changed
                            taskbars = Taskbar.GenerateTaskbarInfo();
                            Debug.WriteLine("Regenerating taskbar info");
                        }

                        for (int current = 0; current < taskbars.Count; current++)
                        {
                            if (taskbars[current].TaskbarHwnd == IntPtr.Zero || taskbars[current].AppListHwnd == IntPtr.Zero)
                            {
                                taskbars = Taskbar.GenerateTaskbarInfo();
                                Debug.WriteLine("Regenerating taskbar info due to a missing handle");
                                break;
                            }
                            // Get the latest quick details of this taskbar
                            Types.Taskbar newTaskbar = Taskbar.GetQuickTaskbarRects(taskbars[current].TaskbarHwnd, taskbars[current].TrayHwnd, taskbars[current].AppListHwnd);


                            // If the taskbar's monitor has a maximised window, reset it so it's "filled"
                            if (Taskbar.TaskbarShouldBeFilled(taskbars[current].TaskbarHwnd, settings))
                            {
                                if (taskbars[current].Ignored == false)
                                {
                                    Taskbar.ResetTaskbar(taskbars[current], settings);
                                    taskbars[current].Ignored = true;
                                }
                                continue;
                            }

                            // Showhide tray on hover
                            if (settings.ShowTrayOnHover)
                            {
                                LocalPInvoke.RECT currentTrayRect = taskbars[current].TrayRect;
                                if (currentTrayRect.Left != 0)
                                {
                                    LocalPInvoke.GetCursorPos(out LocalPInvoke.POINT msPt);
                                    bool isHoveringOverTray = LocalPInvoke.PtInRect(ref currentTrayRect, msPt);
                                    if (isHoveringOverTray && !settings.ShowTray)
                                    {
                                        settings.ShowTray = true;
                                        taskbars[current].Ignored = true;
                                    }
                                    else if (!isHoveringOverTray && settings.ShowTray)
                                    {
                                        settings.ShowTray = false;
                                        taskbars[current].Ignored = true;
                                    }

                                }
                            }


                            // If the taskbar's overall rect has changed, update it. If it's simple, just update. If it's dynamic, check it's a valid change, then update it.
                            if (Taskbar.TaskbarRefreshRequired(taskbars[current], newTaskbar, settings.IsDynamic) || taskbars[current].Ignored || redrawOverride)
                            {
                                Debug.WriteLine($"Refresh required on taskbar {current}");
                                taskbars[current].Ignored = false;
                                int isFullTest = newTaskbar.TrayRect.Left - newTaskbar.AppListRect.Right;
                                mw.interaction.AddLog($"Taskbar: {current} - AppList ends: {newTaskbar.AppListRect.Right} - Tray starts: {newTaskbar.TrayRect.Left} - Total gap: {isFullTest}");
                                if (!settings.IsDynamic || (isFullTest <= taskbars[current].ScaleFactor * 25 && isFullTest > 0 && newTaskbar.TrayRect.Left != 0))
                                {
                                    // Add the rect changes to the temporary list of taskbars
                                    taskbars[current].TaskbarRect = newTaskbar.TaskbarRect;
                                    taskbars[current].AppListRect = newTaskbar.AppListRect;
                                    taskbars[current].TrayRect = newTaskbar.TrayRect;
                                    Taskbar.UpdateSimpleTaskbar(taskbars[current], settings);
                                    mw.interaction.AddLog($"Updated taskbar {current} simply");
                                }
                                else
                                {
                                    if (Taskbar.CheckDynamicUpdateIsValid(taskbars[current], newTaskbar))
                                    {
                                        // Add the rect changes to the temporary list of taskbars
                                        taskbars[current].TaskbarRect = newTaskbar.TaskbarRect;
                                        taskbars[current].AppListRect = newTaskbar.AppListRect;
                                        taskbars[current].TrayRect = newTaskbar.TrayRect;
                                        Taskbar.UpdateDynamicTaskbar(taskbars[current], settings);
                                        mw.interaction.AddLog($"Updated taskbar {current} dynamically");
                                    }
                                }
                            }
                        }
                        mw.taskbarDetails = taskbars;


                    System.Threading.Thread.Sleep(100);
                    }
                }
                catch (TypeInitializationException ex)
                {
                    mw.interaction.AddLog(ex.Message);
                    mw.interaction.AddLog(ex.InnerException.Message);
                    throw ex;
                }
            }
        }
    }
}
