// GettextTool.cs
//
// Author:
//   Lluis Sanchez Gual <lluis@novell.com>
//
// Copyright (c) 2007 Novell, Inc (http://www.novell.com)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//
//

using System;
using System.IO;
using System.Collections.Generic;
using MonoDevelop.Projects;
using MonoDevelop.Core;
using MonoDevelop.Core.ProgressMonitoring;

namespace MonoDevelop.Gettext
{
	public class GettextTool: IApplication
	{
		bool help;
		string file;
		string project;
		
		public int Run (string[] arguments)
		{
			Console.WriteLine ("MonoDevelop Gettext Update Tool");
			foreach (string s in arguments)
				ReadArgument (s);
			
			if (help) {
				Console.WriteLine ("gettext-update [options] [project-file]");
				Console.WriteLine ("--f --file:FILE   Project or solution file to build.");
				Console.WriteLine ("--p --project:PROJECT  Name of the project to build.");
				Console.WriteLine ();
				return 0;
			}
			
			if (file == null) {
				string[] files = Directory.GetFiles (".", "*.mds");
				if (files.Length == 0) {
					Console.WriteLine ("Solution file not found.");
					return 1;
				}
				file = files [0];
			}
			
			ConsoleProgressMonitor monitor = new ConsoleProgressMonitor ();
			monitor.IgnoreLogMessages = true;
			
			CombineEntry centry = Services.ProjectService.ReadCombineEntry (file, monitor);
			monitor.IgnoreLogMessages = false;
			
			Combine combine = centry as Combine;
			if (combine == null) {
				Console.WriteLine ("File is not a solution: " + file);
				return 1;
			}
			
			if (project != null) {
				centry = combine.FindProject (project);
				
				if (centry == null) {
					Console.WriteLine ("The project '" + project + "' could not be found in " + file);
					return 1;
				}
				TranslationProject tp = centry as TranslationProject;
				if (tp == null) {
					Console.WriteLine ("The project '" + centry.FileName + "' is not a translation project");
					return 1;
				}
				tp.UpdateTranslations (monitor);
			}
			else {
				foreach (TranslationProject p in combine.GetAllEntries (typeof(TranslationProject)))
					p.UpdateTranslations (monitor);
			}
			
			return 0;
		}
		
		void ReadArgument (string argument)
		{
			string optionValuePair;
			
			if (argument.StartsWith("--")) {
				optionValuePair = argument.Substring(2);
			}
			else if (argument.StartsWith("/") || argument.StartsWith("-")) {
				optionValuePair = argument.Substring(1);
			} else
				return;
			
			string option;
			string value;
			
			int indexOfEquals = optionValuePair.IndexOf(':');
			if (indexOfEquals > 0) {
				option = optionValuePair.Substring(0, indexOfEquals);
				value = optionValuePair.Substring(indexOfEquals + 1);
			}
			else {
				option = optionValuePair;
				value = null;
			}
			
			switch (option)
			{
				case "f":
				case "file":
				    file = value;
				    break;

				case "help":
				case "?":
				    help = true;
				    break;

				case "p":
				case "project":
				    project = value;
				    break;

				default:
				    throw new Exception("Unknown option '" + option + "'");
			}
		}
	}
}
