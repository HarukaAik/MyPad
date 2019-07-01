﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace MyPad.ViewModels
{
    public class FileTreeNodeViewModel : ViewModelBase
    {
        public static FileTreeNodeViewModel Empty { get; } = new FileTreeNodeViewModel();

        public string FileName { get; }
        public FileTreeNodeViewModel Parent { get; }
        public ObservableCollection<FileTreeNodeViewModel> Children { get; } = new ObservableCollection<FileTreeNodeViewModel>();

        private FileTreeNodeViewModel()
        {
        }

        public FileTreeNodeViewModel(string fileName)
            : this(fileName, null)
        {
            this.Children.Add(Empty);
        }

        public FileTreeNodeViewModel(string fileName, FileTreeNodeViewModel parent)
        {
            this.FileName = fileName;
            this.Parent = parent;
        }

        public void ExploreChildren()
        {
            bool nodeFilter(string path)
            {
                var attribute = File.GetAttributes(path);
                return attribute.HasFlag(FileAttributes.System) == false &&
                    attribute.HasFlag(FileAttributes.Hidden) == false;
            }

            bool existChild(FileTreeNodeViewModel parent)
            {
                if (Directory.Exists(parent.FileName))
                    return Directory.EnumerateFileSystemEntries(parent.FileName, "*", SearchOption.TopDirectoryOnly)
                        .Where(nodeFilter).Any();
                else
                    return false;
            }

            IEnumerable<FileTreeNodeViewModel> getChildren(FileTreeNodeViewModel parent)
            {
                if (Directory.Exists(parent.FileName))
                    return Directory.EnumerateFileSystemEntries(parent.FileName, "*", SearchOption.TopDirectoryOnly)
                        .Where(nodeFilter).Select(p => new FileTreeNodeViewModel(p, parent));
                else
                    return Enumerable.Empty<FileTreeNodeViewModel>();
            }

            try
            {
                this.Children.Clear();
                this.Children.AddRange(getChildren(this));
                this.Children.Where(c => existChild(c)).ForEach(c => c.Children.Add(Empty));
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}