﻿using AiTool3.Conversations;
using Microsoft.CodeAnalysis;
using Newtonsoft.Json;
using System.Data;

namespace AiTool3.Helpers
{
    public static class DataGridViewHelper
    {

        public static void InitialiseDataGridView(DataGridView dgv)
        {
            // hide dgv headers
            dgv.ColumnHeadersVisible = false;
            // Setting the default cell style for the DataGridView
            DataGridViewCellStyle cellStyle = new DataGridViewCellStyle();
            cellStyle.BackColor = Color.Black;
            cellStyle.ForeColor = Color.White;
            cellStyle.WrapMode = DataGridViewTriState.True;

            dgv.DefaultCellStyle = cellStyle;

            // add cols to dgv
            dgv.Columns.Add("ConvGuid", "ConvGuid");
            dgv.Columns.Add("Content", "Content");
            dgv.Columns.Add("Engine", "Engine");
            dgv.Columns.Add("Title", "Title");
            dgv.Columns[0].Visible = false;
            dgv.Columns[0].ReadOnly = true;
            dgv.Columns[1].Visible = false;
            dgv.Columns[1].ReadOnly = true;
            dgv.Columns[2].Visible = false;
            dgv.Columns[2].ReadOnly = true;
            // make the last column fill the parent
            dgv.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            dgv.Columns[3].ReadOnly = true;

            // make the columns wrap text
            //dgv.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            dgv.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;

            // make the selection column thin
            dgv.RowHeadersWidth = 10;


            // populate dgv with the conversation files in the current directory, ordered by date desc
            var files = Directory.GetFiles(Directory.GetCurrentDirectory(), "v3-conversation-*.json").OrderByDescending(f => new FileInfo(f).LastWriteTime);
            foreach (var file in files)
            {
                var conv = JsonConvert.DeserializeObject<BranchedConversation>(File.ReadAllText(file));
                if (!conv.Messages.Any())
                    continue;

                dgv.Rows.Add(conv.ConvGuid, conv.Messages[0].Content, conv.Messages[0].Engine, conv.Title);


            }
        }
    }
}