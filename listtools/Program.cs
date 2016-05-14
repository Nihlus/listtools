//
//  Program.cs
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
using Warcraft.MPQ;

namespace listtools
{
	class MainClass
	{
		public static void Main(string[] args)
		{
			Console.WriteLine("Hello World!");

			// Read the input arguments
			// If optimizing, do
			// Load provided dictionary OR create new one
			// Get all archive paths from directory pointed to

			// foreach archive
			// Load archive
			// If optimized format, get weak signature of the archive and store with listfile
			// Get listfile of the archive
			// Load all words from the listfile into the dictionary
			// end

			// foreach listfile
			// Replace each word with a lower score with the better word
			// Store listfile as specified output format (optimized or flatfile)
			// end

					
			// If generating, do
			// Get all file paths from directory pointed to
			// Store all paths in the specified output format (optimized or flatfile)
		}
	}
}
