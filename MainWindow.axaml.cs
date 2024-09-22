/*
 * 
Copyright (c) 2024, VLDG2712
 * All rights reserved.
 *
 * This source code is licensed under the BSD-style license found in the
 * LICENSE file in the root directory of this source tree. 
 * 
 */

using Avalonia.Controls;
using Avalonia.Interactivity;
using Tmds.DBus.Protocol;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace YT2MP3Sharp;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
    public void DoDownload(object sender, RoutedEventArgs args)
    {
        message.Text = "Button Clicked!"; // TO DO LOG PANEL
    }
}