// 
// DeleteActions.cs
// 
// Author:
//   Mike Krüger <mkrueger@novell.com>
//   Michael Hutchinson <mhutchinson@novell.com>
// 
// Copyright (C) 2007-2008 Novell, Inc (http://www.novell.com)
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
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

using Gtk;
using Mono.TextEditor.Highlighting;

namespace Mono.TextEditor
{
	public static class DeleteActions
	{
		public static Action<TextEditorData> FromMoveAction (Action<TextEditorData> moveAction)
		{
			return delegate (TextEditorData data) {
				SelectionActions.StartSelection (data);
				moveAction (data);
				SelectionActions.EndSelection (data);
				data.DeleteSelectedText ();
			};
		}
		
		static void PreviousWord (TextEditorData data, bool subword)
		{
			int oldLine = data.Caret.Line;

			int caretOffset = data.Caret.Offset;
			int offset = subword ? data.FindPrevSubwordOffset (caretOffset) : data.FindPrevWordOffset (caretOffset);
			if (caretOffset != offset && data.CanEdit (oldLine) && data.CanEdit (data.Caret.Line)) {
				data.Remove (offset, caretOffset - offset);
				data.Caret.Offset = offset;
			}
		}
		
		static void NextWord (TextEditorData data, bool subword)
		{
			int oldLine = data.Caret.Line;
			int caretOffset = data.Caret.Offset;
			int offset = subword? data.FindNextSubwordOffset (caretOffset) : data.FindNextWordOffset (caretOffset);
			if (caretOffset != offset && data.CanEdit (oldLine) && data.CanEdit (data.Caret.Line))  {
				data.Remove (caretOffset, offset - caretOffset);
			}
		}
		
		public static void PreviousWord (TextEditorData data)
		{
			PreviousWord (data, false);
		}
		
		public static void PreviousSubword (TextEditorData data)
		{
			PreviousWord (data, true);
		}
		
		public static void NextWord (TextEditorData data)
		{
			NextWord (data, false);
		}
		
		public static void NextSubword (TextEditorData data)
		{
			NextWord (data, true);
		}
		
		public static void CaretLine (TextEditorData data)
		{
			if (data.Document.LineCount <= 1 || !data.CanEdit (data.Caret.Line))
				return;
			LineSegment line = data.Document.GetLine (data.Caret.Line);
			data.Remove (line.Offset, line.Length);
			data.Caret.Column = DocumentLocation.MinColumn;
		}
		
		public static void CaretLineToEnd (TextEditorData data)
		{
			if (!data.CanEdit (data.Caret.Line))
				return;
			var line = data.Document.GetLine (data.Caret.Line);

			using (var undo = data.OpenUndoGroup ()) {
				data.EnsureCaretIsNotVirtual ();
				int physColumn = data.Caret.Column - 1;
				if (physColumn == line.EditableLength) {
					// Nothing after the cursor, delete the end-of-line sequence
					data.Remove (line.Offset + physColumn, line.Length - physColumn);
				} else {
					// Delete from cursor position to the end of the line
					data.Remove (line.Offset + physColumn, line.EditableLength - physColumn);
				}
			}
			data.Document.CommitLineUpdate (data.Caret.Line);
		}
		
		public static void Backspace (TextEditorData data)
		{
			Backspace (data, RemoveCharBeforeCaret);
		}
		
		public static void Backspace (TextEditorData data, Action<TextEditorData> removeCharBeforeCaret)
		{
			if (!data.CanEditSelection)
				return;
			if (data.IsSomethingSelected) {
				// case: zero width block selection
				if (data.MainSelection.SelectionMode == SelectionMode.Block && data.MainSelection.Anchor.Column == data.MainSelection.Lead.Column) {
					var col = data.MainSelection.Lead.Column;
					if (col <= DocumentLocation.MinColumn) {
						data.ClearSelection ();
						return;
					}
					bool preserve = data.Caret.PreserveSelection;
					data.Caret.PreserveSelection = true;
					col--;
					for (int lineNumber = data.MainSelection.MinLine; lineNumber <= data.MainSelection.MaxLine; lineNumber++) {
						data.Remove (data.Document.GetLine (lineNumber).Offset + col - 1, 1);
					}
					data.MainSelection.Lead = new DocumentLocation (data.MainSelection.Lead.Line, col);
					data.MainSelection.Anchor = new DocumentLocation (data.MainSelection.Anchor.Line, col);
					data.Caret.PreserveSelection = preserve;
					data.Document.CommitMultipleLineUpdate (data.MainSelection.MinLine, data.MainSelection.MaxLine);
					return;
				}
				data.DeleteSelectedText (data.MainSelection.SelectionMode != SelectionMode.Block);
				return;
			}

			if (data.Caret.Line == DocumentLocation.MinLine && data.Caret.Column == DocumentLocation.MinColumn)
				return;
			
			// Virtual indentation needs to be fixed before to have the same behavior
			// if it's there or not (otherwise user has to press multiple backspaces in some cases)
			data.FixVirtualIndentation ();
			LineSegment line = data.Document.GetLine (data.Caret.Line);
			if (data.Caret.Column > line.EditableLength + 1) {
				data.Caret.Column = line.EditableLength + 1;
			} else if (data.Caret.Offset == line.Offset) {
				LineSegment lineAbove = data.Document.GetLine (data.Caret.Line - 1);
				data.Remove (lineAbove.EndOffset - lineAbove.DelimiterLength, lineAbove.DelimiterLength);
			} else {
				removeCharBeforeCaret (data);
			}
			
			// Needs to be fixed after, the line may just contain the indentation
			data.FixVirtualIndentation ();
		}
		
		public static void RemoveCharBeforeCaret (TextEditorData data)
		{
			int offset = data.Caret.Offset;
			if (offset <= 0)
				return;
			data.Remove (offset - 1, 1);
		}
		
		public static void Delete (TextEditorData data)
		{
			if (!data.CanEditSelection)
				return;
			if (data.IsSomethingSelected) {
				// case: zero width block selection
				if (data.MainSelection.SelectionMode == SelectionMode.Block && data.MainSelection.Anchor.Column == data.MainSelection.Lead.Column) {
					var col = data.MainSelection.Lead.Column;
					if (col <= DocumentLocation.MinColumn) {
						data.ClearSelection ();
						return;
					}
					bool preserve = data.Caret.PreserveSelection;
					data.Caret.PreserveSelection = true;
					col--;
					for (int lineNumber = data.MainSelection.MinLine; lineNumber <= data.MainSelection.MaxLine; lineNumber++) {
						LineSegment lineSegment = data.Document.GetLine (lineNumber);
						if (col < lineSegment.EditableLength)
							data.Remove (lineSegment.Offset + col, 1);
					}
					data.Caret.PreserveSelection = preserve;
					data.Document.CommitMultipleLineUpdate (data.MainSelection.MinLine, data.MainSelection.MaxLine);
					return;
				}
				data.DeleteSelectedText (data.MainSelection.SelectionMode != SelectionMode.Block);
				return;
			}
			if (data.Caret.Offset >= data.Document.TextLength)
				return;

			using (var undoGroup = data.OpenUndoGroup()) {
				data.EnsureCaretIsNotVirtual ();

				LineSegment line = data.Document.GetLine (data.Caret.Line);
				if (data.Caret.Column == line.EditableLength + 1) {
					if (data.Caret.Line < data.Document.LineCount) { 
						data.Remove (line.EndOffset - line.DelimiterLength, line.DelimiterLength);
						if (line.EndOffset == data.Document.TextLength)
							line.DelimiterLength = 0;
					}
				} else {
					data.Remove (data.Caret.Offset, 1); 
					data.Document.CommitLineUpdate (data.Caret.Line);
				}
				data.FixVirtualIndentation ();
			}
		}
		
		public static void DeleteSelection (TextEditorData data)
		{
			if (!data.CanEditSelection)
				return;
			if (data.IsSomethingSelected) {
				data.DeleteSelectedText ();
				return;
			}
		}
	}
}
