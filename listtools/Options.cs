//
//  Options.cs
//
//  Author:
//       Jarl Gullberg <jarl.gullberg@gmail.com>
//
//  Copyright (c) 2016 Jarl Gullberg
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.
//
using System;
using System.IO;
using CommandLine;
using CommandLine.Text;
using liblistfile;

namespace listtools
{
	/// <summary>
	/// Command-line option definitions.
	/// </summary>
	public class Options
	{
		/// <summary>
		/// Gets or sets the input path to a folder containing MPQ archives. Subfolders are
		/// searched as well.
		/// </summary>
		/// <value>The input path.</value>
		[Option('i', "input", Required = true,
			HelpText = "Selects the input directory which will be used for processing.")]
		public string InputPath
		{
			get;
			set;
		}

		/// <summary>
		/// Gets or sets the output path where the generated listfiles will be stored.
		/// </summary>
		/// <value>The output path.</value>
		[Option('o', "output", Required = true,
			HelpText = "Selects the output directory which will be used for processing.")]
		public string OutputPath
		{
			get;
			set;
		}

		/// <summary>
		/// Gets or sets the input dictionary from which the correct terms are gathered.
		/// </summary>
		/// <value>The input dictionary.</value>
		[Option('d', "dictionary",
			HelpText = "Selects the input dictionary which will be used for processing.")]
		public string DictionaryPath
		{
			get;
			set;
		}

		/// <summary>
		/// Gets or sets the task type.
		/// </summary>
		/// <value>The task type.</value>
		[Option('t', "task-type", DefaultValue = TaskType.Generate,
			HelpText = "Sets the task the program should perform. Valid options are \"generate\" and \"optimize\". The options are case-insensitive.")]
		public TaskType SelectedTask
		{
			get;
			set;
		}

		/// <summary>
		/// Gets or sets the output format.
		/// </summary>
		/// <value>The input path.</value>
		[Option('f', "format", DefaultValue = OutputFormat.Flatfile,
			HelpText = "Sets the output format of listfiles. Valid options are \"flatfile\" and \"compressed\". When generating a listfile, only flatfiles can be generated. The options are case-insensitive.")]
		public OutputFormat Format
		{
			get;
			set;
		}

		/// <summary>
		/// Gets or sets a value indicating whether this <see cref="listtools.Options"/> is verbose.
		/// </summary>
		/// <value><c>true</c> if verbose; otherwise, <c>false</c>.</value>
		[Option('v', "verbose", DefaultValue = true,
			HelpText = "Prints all messages to standard output.")]
		public bool Verbose
		{
			get;
			set;
		}

		/// <summary>
		/// Gets or sets the last state of the parser.
		/// </summary>
		/// <value>The last state of the parser.</value>
		[ParserState]
		public IParserState LastParserState
		{
			get;
			set;
		}

		/// <summary>
		/// Gets a value indicating whether this instance is using the default dictionary.
		/// </summary>
		/// <value><c>true</c> if this instance is using default dictionary; otherwise, <c>false</c>.</value>
		public bool IsUsingDefaultDictionary
		{
			get
			{
				return this.DictionaryPath == Options.GetDefaultDictionaryPath();
			}
		}

		/// <summary>
		/// Gets the default dictionary path.
		/// </summary>
		/// <returns>The default dictionary path.</returns>
		public static string GetDefaultDictionaryPath()
		{
			string applicationDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			string dictionaryPath = String.Format("{0}{1}listtools{1}dictionary.{2}", applicationDataPath,
				                        Path.DirectorySeparatorChar, ListfileDictionary.Extension);

			return dictionaryPath;
		}

		/// <summary>
		/// Gets the generated help text.
		/// </summary>
		/// <returns>The usage.</returns>
		public string GetUsage()
		{
			return HelpText.AutoBuild(this, current => HelpText.DefaultParsingErrorsHandler(this, current));
		}
	}

	/// <summary>
	/// The type of task the tool will perform.
	/// </summary>
	public enum TaskType
	{
		/// <summary>
		/// Generates a new listfile.
		/// </summary>
		Generate = 0,

		/// <summary>
		/// Optimizes an existing set of archives.
		/// </summary>
		Optimize = 1
	}

	/// <summary>
	/// The output format the tool will use.
	/// </summary>
	public enum OutputFormat
	{
		/// <summary>
		/// Flatfile. Simple lines in a text document.
		/// </summary>
		Flatfile = 0,

		/// <summary>
		/// Compressed format. Stores listfiles in a zipped binary format.
		/// </summary>
		Compressed = 1
	}
}

