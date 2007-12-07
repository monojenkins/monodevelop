//
// TranslationProject.cs
//
// Author:
//   Mike Krüger <mkrueger@novell.com>
//
// Copyright (C) 2007 Novell, Inc (http://www.novell.com)
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

using MonoDevelop.Core;
using MonoDevelop.Core.Execution;
using MonoDevelop.Projects;
using MonoDevelop.Projects.Serialization;
using MonoDevelop.Gettext.Editor;
using MonoDevelop.Deployment;

namespace MonoDevelop.Gettext
{	
	public class TranslationProject : CombineEntry, IDeployable
	{
		[ItemProperty("packageName")]
		string packageName = null;
		
		[ItemProperty("outputType")]
		TranslationOutputType outputType;
			
		[ItemProperty("relPath")]
		string relPath = null;
		
		[ItemProperty("absPath")]
		string absPath = null;
		
		public string PackageName {
			get { return packageName; }
			set { packageName = value; }
		}
		
		public string RelPath {
			get { return relPath; }
			set { relPath = value; }
		}
		
		public string AbsPath {
			get { return absPath; }
			set { absPath = value; }
		}
		
		public TranslationOutputType OutputType {
			get { return outputType; }
			set { outputType = value; }
		}
		
		[ItemProperty]
		List<Translation> translations = new List<Translation> ();
		
		[ItemProperty]
		List<TranslationProjectInformation> projectInformations = new List<TranslationProjectInformation> ();
		
		public ReadOnlyCollection<Translation> Translations {
			get { return translations.AsReadOnly (); }
		}
		
		public ReadOnlyCollection<TranslationProjectInformation> TranslationProjectInformations {
			get { return projectInformations.AsReadOnly (); }
		}
		
		public TranslationProject ()
		{
			this.NeedsBuilding = true;
			isDirty = true;
		}
		
		public TranslationProjectInformation GetProjectInformation (CombineEntry entry, bool force)
		{
			foreach (TranslationProjectInformation info in this.projectInformations) {
				if (info.ProjectName == entry.Name)
					return info;
			}
			if (force) {
				TranslationProjectInformation newInfo = new TranslationProjectInformation (entry.Name);
				this.projectInformations.Add (newInfo);
				return newInfo;
			}
			return null;
		}
		
		public bool IsIncluded (CombineEntry entry)
		{
			TranslationProjectInformation info = GetProjectInformation (entry, false);
			if (info != null)
				return info.IsIncluded;
			return true;
		}
		
		public override void InitializeFromTemplate (XmlElement template)
		{
			OutputType  = (TranslationOutputType)Enum.Parse (typeof(TranslationOutputType), template.GetAttribute ("outputType"));
			PackageName = template.GetAttribute ("packageName");
			RelPath     = template.GetAttribute ("relPath");
			AbsPath     = template.GetAttribute ("absPath");
		}
		
		string GetFileName (string isoCode)
		{
			return Path.Combine (base.BaseDirectory, isoCode + ".po");
		}
		
		public class MatchLocation
		{
			string originalString;
			string originalPluralString;
			int    line;
			
			public string OriginalString {
				get { return originalString; }
			}
			
			public string OriginalPluralString {
				get { return originalPluralString; }
			}
			
			public int Line {
				get { return line; }
			}
			
			public MatchLocation (string originalString, string originalPluralString, int line)
			{
				this.originalString = originalString;
				this.originalPluralString = originalPluralString;
				this.line = line;
			}
			
			public MatchLocation (string originalString, int line) : this (originalString, null, line)
			{
			}
		}
		

		public Translation AddNewTranslation (string isoCode, IProgressMonitor monitor)
		{
			try {
				Translation tr = new Translation (this, isoCode);
				translations.Add (tr);
				string templateFile    = Path.Combine (this.BaseDirectory, "messages.po");
				string translationFile = GetFileName (isoCode);
				if (!File.Exists (templateFile)) 
					CreateDefaultCatalog (monitor);
				File.Copy (templateFile, translationFile);
				
				monitor.ReportSuccess (String.Format (GettextCatalog.GetString ("Language '{0}' successfully added."), isoCode));
				monitor.Step (1);
				isDirty = true; 
				this.Save (monitor);
				OnTranslationAdded (EventArgs.Empty);
				return tr;
			} catch (Exception e) {
				monitor.ReportError (String.Format ( GettextCatalog.GetString ("Language '{0}' could not be added: "), isoCode), e);
				return null;
			} finally {
				monitor.EndTask ();
			}
		}
		
		public Translation GetTranslation (string isoCode)
		{
			foreach (Translation translation in this.translations) {
				if (translation.IsoCode == isoCode) 
					return translation;
			}
			return null;
		}
		
		public void RemoveTranslation (string isoCode)
		{
			Translation translation = GetTranslation (isoCode);
			if (translation != null) {
				this.translations.Remove (translation);
				OnTranslationRemoved (EventArgs.Empty);
				isDirty = true;
			}
		}
		
		public override IConfiguration CreateConfiguration (string name)
		{
			return new TranslationProjectConfiguration (name);
		}
		
		internal string OutputDirectory {
			get {
				if (this.ParentCombine.StartupEntry == null) 
					return BaseDirectory;
				if (this.ParentCombine.StartupEntry is DotNetProject) {
					return Path.Combine (Path.GetDirectoryName (((DotNetProject)RootCombine.StartupEntry).GetOutputFileName ()), RelPath);
				}
				return Path.Combine (this.RootCombine.StartupEntry.BaseDirectory, RelPath);
			}
		}
		
		void CreateDefaultCatalog (IProgressMonitor monitor)
		{
			IFileScanner[] scanners = TranslationService.GetFileScanners ();
			
			Catalog catalog = new Catalog ();
			List<Project> projects = new List<Project> ();
			foreach (Project p in RootCombine.GetAllProjects ()) {
				if (IsIncluded (p))
					projects.Add (p);
			}
			foreach (Project p in projects) {
				monitor.Log.WriteLine (GettextCatalog.GetString ("Scanning project {0}...", p.Name));
				foreach (ProjectFile file in p.ProjectFiles) {
					if (monitor.IsCancelRequested)
						return;
					if (!File.Exists (file.FilePath))
						continue;
					if (file.Subtype == Subtype.Code) {
						string mimeType = Runtime.PlatformService.GetMimeTypeForUri (file.FilePath);
						foreach (IFileScanner fs in scanners) {
							if (fs.CanScan (this, catalog, file.FilePath, mimeType))
								fs.UpdateCatalog (this, catalog, monitor, file.FilePath);
						}
					}
				}
				monitor.Step (1);
			}
			catalog.Save (Path.Combine (this.BaseDirectory, "messages.po"));
		}
		
		public void UpdateTranslations (IProgressMonitor monitor)
		{
			monitor.BeginTask (null, Translations.Count + 1);
			
			try {
				List<Project> projects = new List<Project> ();
				foreach (Project p in RootCombine.GetAllProjects ()) {
					if (IsIncluded (p))
						projects.Add (p);
				}
				monitor.BeginTask (GettextCatalog.GetString ("Updating message catalog"), projects.Count);
				CreateDefaultCatalog (monitor);
				monitor.Log.WriteLine (GettextCatalog.GetString ("Done"));
			} finally { 
				monitor.EndTask ();
				monitor.Step (1);
			}
			if (monitor.IsCancelRequested) {
				monitor.Log.WriteLine (GettextCatalog.GetString ("Operation cancelled."));
				return;
			}
			
			foreach (Translation translation in this.Translations) {
				string poFileName  = translation.PoFile;
				monitor.BeginTask (GettextCatalog.GetString ("Updating {0}", translation.PoFile), 1);
				try {
					Runtime.ProcessService.StartProcess ("msgmerge",
					                                     " -U " + poFileName + " -v " + Path.Combine (this.BaseDirectory, "messages.po"),
					                                     this.BaseDirectory,
					                                     monitor.Log,
					                                     monitor.Log,
					                                     null).WaitForOutput ();
				} catch (Exception ex) {
					monitor.ReportError (GettextCatalog.GetString ("Could not update file {0}", translation.PoFile), ex);
				} finally {
					monitor.EndTask ();
					monitor.Step (1);
				}
				if (monitor.IsCancelRequested) {
					monitor.Log.WriteLine (GettextCatalog.GetString ("Operation cancelled."));
					return;
				}
			}
		}
		public void RemoveEntry (string msgstr)
		{
			foreach (Translation translation in this.Translations) {
				string poFileName  = translation.PoFile;
				Catalog catalog = new Catalog ();
				catalog.Load (new MonoDevelop.Core.ProgressMonitoring.NullProgressMonitor (), poFileName);
				CatalogEntry entry = catalog.FindItem (msgstr);
				if (entry != null) {
					catalog.RemoveItem (entry);
					catalog.Save (poFileName);
				}
			}
		}
		
		public override void Deserialize (ITypeSerializer handler, DataCollection data)
		{
			base.Deserialize (handler, data);
			foreach (Translation translation in this.Translations)
				translation.ParentProject = this;
		}

				
		protected override ICompilerResult OnBuild (IProgressMonitor monitor)
		{
			CompilerResults results = new CompilerResults (null);
			string outputDirectory = OutputDirectory;
			if (!string.IsNullOrEmpty (outputDirectory)) {
				foreach (Translation translation in this.Translations) {
					string poFileName  = translation.PoFile;
					string moDirectory = Path.Combine (Path.Combine (outputDirectory, translation.IsoCode), "LC_MESSAGES");
					if (!Directory.Exists (moDirectory))
						Directory.CreateDirectory (moDirectory);
					string moFileName  = Path.Combine (moDirectory, PackageName + ".mo");
					
					ProcessWrapper process = Runtime.ProcessService.StartProcess ("msgfmt",
				                                     poFileName + " -o " + moFileName,
				                                     this.BaseDirectory,
				                                     monitor.Log,
				                                     monitor.Log,
				                                     null);
					process.WaitForExit ();

					if (process.ExitCode == 0) {
						monitor.Log.WriteLine (GettextCatalog.GetString ("Translation {0}: Compilation succeeded.", translation.IsoCode));
					} else {
						string error   = process.StandardError.ReadToEnd ();
						string message = String.Format (GettextCatalog.GetString ("Translation {0}: Compilation failed. Reason: {1}"), translation.IsoCode, error);
						monitor.Log.WriteLine (message);
						results.Errors.Add (new CompilerError (this.Name, 0, 0, null, message));
					}
				}
				isDirty = false;
				this.NeedsBuilding = false;
			}
			return new DefaultCompilerResult (results, "");
		}
		
		protected override void OnClean (IProgressMonitor monitor)
		{
			isDirty = true;
			this.NeedsBuilding = true;
			monitor.Log.WriteLine (GettextCatalog.GetString ("Removing all .mo files."));
			string outputDirectory = OutputDirectory;
			if (string.IsNullOrEmpty (outputDirectory))
				return;
			foreach (Translation translation in this.Translations) {
				string moDirectory = Path.Combine (Path.Combine (outputDirectory, translation.IsoCode), "LC_MESSAGES");
				string moFileName  = Path.Combine (moDirectory, PackageName + ".mo");
				if (File.Exists (moFileName)) 
					File.Delete (moFileName);
			}
		}
		
		protected override void OnExecute (IProgressMonitor monitor, MonoDevelop.Projects.ExecutionContext context)
		{
		}
		
#region Deployment
		public DeployFileCollection GetDeployFiles ()
		{
			DeployFileCollection result = new DeployFileCollection ();
			foreach (Translation translation in this.Translations) {
				if (OutputType == TranslationOutputType.SystemPath) {
					string moDirectory = Path.Combine ("locale", translation.IsoCode);
					moDirectory = Path.Combine (moDirectory, "LC_MESSAGES");
					string moFileName  = Path.Combine (moDirectory, PackageName + ".mo");
					result.Add (new DeployFile (this, translation.OutFile, moFileName, TargetDirectory.CommonApplicationDataRoot));
				} else {
					string moDirectory = Path.Combine (RelPath, translation.IsoCode);
					moDirectory = Path.Combine (moDirectory, "LC_MESSAGES");
					string moFileName  = Path.Combine (moDirectory, PackageName + ".mo");
					result.Add (new DeployFile (this, translation.OutFile, moFileName, TargetDirectory.ProgramFiles));
				}
			}
			return result;
		}
#endregion
		
		bool isDirty = true;
		protected override bool OnGetNeedsBuilding ()
		{
			return this.isDirty;
		}
		
		protected override void OnSetNeedsBuilding (bool val)
		{
			isDirty = val;
		}
		
		protected virtual void OnTranslationAdded (EventArgs e)
		{
			if (TranslationAdded != null)
				TranslationAdded (this, e);
		}
		
		public event EventHandler TranslationAdded;
		
		protected virtual void OnTranslationRemoved (EventArgs e)
		{
			if (TranslationRemoved != null)
				TranslationRemoved (this, e);
		}
		
		public event EventHandler TranslationRemoved;
	}
	
	public enum TranslationOutputType {
		RelativeToOutput,
		SystemPath
	}
	
	public class TranslationProjectConfiguration : IConfiguration
	{
		[ItemProperty]
		string name;
		
		public string Name {
			get {
				return name;
			}
		}
		
		public TranslationProjectConfiguration ()
		{
		}
		
		public TranslationProjectConfiguration (string name)
		{
			this.name = name;
		}

		public object Clone ()		
		{
			IConfiguration conf = (IConfiguration) MemberwiseClone ();
			conf.CopyFrom (this);
			return conf;
		}
		
		public virtual void CopyFrom (IConfiguration configuration)
		{
		}
	}
	
	public class TranslationProjectInformation
	{
		[ItemProperty]
		string projectName;
		
		[ItemProperty]
		bool isIncluded;
		
		public string ProjectName {
			get { return projectName; }
			set { projectName = value; }
		}
		
		public bool IsIncluded {
			get { return isIncluded; }
			set { isIncluded = value; }
		}
		
		public TranslationProjectInformation ()
		{
		}
		
		public TranslationProjectInformation (string projectName)
		{
			this.projectName = projectName;
		}
	}
}
