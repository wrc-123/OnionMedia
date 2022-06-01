﻿/*
 * Copyright (C) 2022 Jaden Phil Nebel (Onionware)
 * This program is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, version 3.
 * 
 * This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU Affero General Public License for more details.
 * 
 * You should have received a copy of the GNU Affero General Public License along with this program. If not, see <https://www.gnu.org/licenses/>
 */

using CommunityToolkit.WinUI;
using FFMpegCore;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OnionMedia.Core.Models;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
using YoutubeDLSharp.Options;

namespace OnionMedia
{
    internal static class GlobalResources
    {
        public static HardwareEncoder[] HardwareEncoders { get; } = Enum.GetValues<HardwareEncoder>();
        public static AudioConversionFormat[] AudioConversionFormats { get; } = Enum.GetValues<AudioConversionFormat>().Take(Range.StartAt(2)).ToArray();
        public static LibraryInfo[] LibraryLicenses { get; } =
        {
            new LibraryInfo("FFmpeg", "FFmpeg 64-bit static Windows build from www.gyan.dev", "GNU GPL v3", Installpath + @"\ExternalBinaries\ffmpeg+yt-dlp\FFmpeg_LICENSE", "https://github.com/FFmpeg/FFmpeg/commit/9687cae2b4"),
            new LibraryInfo("yt-dlp", "yt-dlp", "Unlicense", LicensesDir + "yt-dlp.txt", "https://github.com/yt-dlp/yt-dlp"),
            new LibraryInfo("CommunityToolkit", "Microsoft", "MIT License", LicensesDir + "communitytoolkit.txt", "https://github.com/CommunityToolkit/WindowsCommunityToolkit"),
            new LibraryInfo("FFMpegCore", "Vlad Jerca", "MIT License", LicensesDir + "FFMpegCore.txt", "https://github.com/rosenbjerg/FFMpegCore"),
            new LibraryInfo("TagLib#", "mono", "LGPL v2.1", LicensesDir + "TagLibSharp.txt", "https://github.com/mono/taglib-sharp"),
            new LibraryInfo("xFFmpeg.NET", "Tobias Haimerl(cmxl)", "MIT License", LicensesDir + "xFFmpeg.NET.txt", "https://github.com/cmxl/FFmpeg.NET"),
            new LibraryInfo("YoutubeDLSharp", "Bluegrams", "BSD 3-Clause License", LicensesDir + "YoutubeDLSharp.txt", "https://github.com/Bluegrams/YoutubeDLSharp"),
            new LibraryInfo("YoutubeExplode", "Tyrrrz", "LGPL v3", LicensesDir + "YoutubeExplode.txt", "https://github.com/Tyrrrz/YoutubeExplode")
        };

        public static string Installpath => Windows.Application­Model.Package.Current.Installed­Location.Path;
        public static string LocalPath => ApplicationData.Current.LocalFolder.Path;
        public static string Tempdir => Path.GetTempPath() + @"\Onionmedia";
        public static string ConverterTempdir => Tempdir + @"\Converter";
        public static string DownloaderTempdir => Tempdir + @"\Downloader";
        public static string FFmpegPath => Installpath + @"\ExternalBinaries\ffmpeg+yt-dlp\binaries\ffmpeg.exe";
        public static string YtDlPath => Installpath + @"\ExternalBinaries\ffmpeg+yt-dlp\binaries\yt-dlp.exe";
        public static string LicensesDir => Installpath + @"\third-party-licenses\";
        public static int SystemThreadCount => Environment.ProcessorCount;
        public static FFmpegCodecConfig FFmpegCodecs { get; set; }
        public static DispatcherQueue DispatcherQueue { get; set; }
        public static XamlRoot XamlRoot { get; set; }

        public const string INVALIDFILENAMECHARACTERSREGEX = @"[<|>:""/\?*]";
        public const string FFMPEGTIMEFROMOUTPUTREGEX = "time=[0-9]{2}:[0-9]{2}:[0-9]{2}.[0-9]{2}";
        public const string URLREGEX = @"^(?:https?:\/\/)?(?:www[.])?\S+[.]\S+(?:[\/]+\S*)*$";
        private const string DialogResources = "DialogResources";


        //Shared methods
        public static async Task DisplayFileSaveErrorDialog(uint unauthorizedAccessExceptions, uint directoryNotFoundExceptions, uint notEnoughSpaceExceptions)
        {
            var dialog = new ContentDialog()
            {
                Content = new TextBlock() { TextWrapping = TextWrapping.WrapWholeWords },
                XamlRoot = XamlRoot,
                PrimaryButtonText = "OK"
            };

            if (MultipleExceptionTypes(unauthorizedAccessExceptions, directoryNotFoundExceptions, notEnoughSpaceExceptions))
            {
                dialog.Title = "conversionFilesCantBeSavedTitle".GetLocalized(DialogResources);
                ((TextBlock)dialog.Content).Text = "conversionFilesCantBeSaved".GetLocalized(DialogResources).Replace("{0}", (unauthorizedAccessExceptions + directoryNotFoundExceptions).ToString());
                await dialog.ShowAsync();
                return;
            }
            if (unauthorizedAccessExceptions > 0)
            {
                dialog.Title = "conversionFilesNoWriteAccessTitle".GetLocalized(DialogResources);
                ((TextBlock)dialog.Content).Text = "conversionFilesNoWriteAccess".GetLocalized(DialogResources).Replace("{0}", unauthorizedAccessExceptions.ToString());
                await dialog.ShowAsync();
                return;
            }
            if (directoryNotFoundExceptions > 0)
            {
                dialog.Title = "conversionFilesPathNotFoundTitle".GetLocalized(DialogResources);
                ((TextBlock)dialog.Content).Text = "conversionFilesPathNotFound".GetLocalized(DialogResources).Replace("{0}", directoryNotFoundExceptions.ToString());
                await dialog.ShowAsync();
                return;
            }
            if (notEnoughSpaceExceptions > 0)
            {
                dialog.Title = "notEnoughSpaceTitle".GetLocalized(DialogResources);
                ((TextBlock)dialog.Content).Text = "notEnoughSpace".GetLocalized(DialogResources).Replace("{0}", notEnoughSpaceExceptions.ToString());
                await dialog.ShowAsync();
                return;
            }
        }

        private static bool MultipleExceptionTypes(params uint[] amountOfException) => amountOfException != null && amountOfException.Count(n => n > 0) > 1;

        public static long CalculateVideoBitrate(string filepath, IMediaAnalysis meta)
        {
            if (filepath == null || meta == null)
                throw new ArgumentNullException(filepath == null ? nameof(filepath) : nameof(meta));
            if (!File.Exists(filepath))
                throw new FileNotFoundException();

            long audioBytes = meta.PrimaryAudioStream?.BitRate * (int)meta.Duration.TotalSeconds ?? 0;
            long sizeWithoutAudio = new FileInfo(filepath).Length - audioBytes;
            return sizeWithoutAudio / (int)meta.Duration.TotalSeconds * 8;
        }


        /// <summary>
        /// Opens a FolderPicker and lets the user choose a location.
        /// </summary>
        /// <returns>The path to the folder, or null if the dialog is canceled.</returns>
        public static async Task<string> SelectFolderPathAsync()
        {
            FolderPicker picker = new();
            picker.SuggestedStartLocation = PickerLocationId.VideosLibrary;
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
            var result = await picker.PickSingleFolderAsync();
            return result?.Path;
        }

#if DEBUG
        public const bool IS_DEBUG = true;
#else
        public const bool IS_DEBUG = false;
#endif
    }
}
