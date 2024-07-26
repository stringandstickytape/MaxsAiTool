﻿using AiTool3.ApiManagement;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AiTool3.Settings
{
    public partial class SettingsForm : Form
    {
        public int yInc = 32;

        public SettingsSet NewSettings;
        public SettingsForm(SettingsSet settings)
        {
            InitializeComponent();

            NewSettings = CloneSettings(settings);

            InitializeDgvModels();
            CreateDgvColumns();

            dgvModels.CellClick += DgvModels_CellClick;

            CreateDgvRows(settings);

            var ypos = 0;

            // for every public bool property on settings...
            foreach (var prop in settings.GetType().GetProperties().Where(p => p.PropertyType == typeof(bool)))
            {
                var displayNameAttr = prop.GetCustomAttribute<MyDisplayNameAttrAttribute>();
                if (displayNameAttr == null) continue;
                // ... create a new checkbox control
                var cb = new CheckBox
                {
                    Text = displayNameAttr.DisplayName,
                    Checked = (bool)prop.GetValue(settings),
                    AutoSize = true,
                    Location = new Point(0, ypos)
                };

                cb.Click += (s, e) =>
                {
                    prop.SetValue(NewSettings, cb.Checked);
                };

                // add the control to panelToggles
                panelToggles.Controls.Add(cb);



                // increment ypos
                ypos += yInc;

            }

            // for every public string property on settings...
            foreach (var prop in settings.GetType().GetProperties().Where(p => p.PropertyType == typeof(string)))
            {
                var displayNameAttr = prop.GetCustomAttribute<MyDisplayNameAttrAttribute>();

                var isPathAttr = prop.GetCustomAttribute<IsPathAttribute>();
                var isFileAttr = prop.GetCustomAttribute<IsFileAttribute>();
                var takesBrowserDialog = isPathAttr != null || isFileAttr != null;

                if (displayNameAttr == null) continue;
                // create a new textbox control
                var tb = new TextBox
                {
                    Text = (string)prop.GetValue(settings),
                    Location = new Point(0, ypos),
                    Width = 600
                };

                tb.TextChanged += (s, e) =>
                {
                    prop.SetValue(NewSettings, tb.Text);
                };

                // add to panel
                panelToggles.Controls.Add(tb);

                // add matching label to the right
                var lbl = new Label
                {
                    Text = displayNameAttr.DisplayName,
                    AutoSize = true,
                    Location = new Point(tb.Width + 5 + (takesBrowserDialog != null ? 60 : 0), ypos)
                };
                panelToggles.Controls.Add(lbl);

                // does the prop have an IsPathAttribute?
                
                if (takesBrowserDialog != null)
                {
                    var btn = new Button
                    {
                        Text = "...",
                        Location = new Point(tb.Width +10, ypos),
                        Width = 50,
                        Height = 30
                    };

                    btn.Click += (s, e) =>
                    {
                        if(isPathAttr != null)
                        {
                            var dialog = new FolderBrowserDialog();
                            if (dialog.ShowDialog() == DialogResult.OK)
                            {
                                tb.Text = dialog.SelectedPath;
                            }
                        }
                        else if(isFileAttr != null)
                        {
                            var ext = isFileAttr.Extension;

                            var dialog = new OpenFileDialog();
                            dialog.Filter = $"{ext} files (*{ext})|*{ext}|All files (*.*)|*.*";
                            if (tb.Text != "")
                            {
                                dialog.InitialDirectory = tb.Text;
                            }
                            if (dialog.ShowDialog() == DialogResult.OK)
                            {
                                tb.Text = dialog.FileName;
                            }
                        }
                    };

                    panelToggles.Controls.Add(btn);
                }


                ypos += yInc;
            }

            // for every public int property on settings...
            foreach (var prop in settings.GetType().GetProperties().Where(p => p.PropertyType == typeof(int)))
            {
                var displayNameAttr = prop.GetCustomAttribute<MyDisplayNameAttrAttribute>();
                if (displayNameAttr == null) continue;


                // create a new numeric up down control
                var nud = new NumericUpDown
                {
                    Minimum = 0,
                    Maximum = 65535,
                    Location = new Point(0, ypos-1)
                };

                nud.Value = (int)prop.GetValue(settings);

                // click
                nud.ValueChanged += (s, e) =>
                {
                    prop.SetValue(NewSettings, (int)(nud.Value));
                };
                
                // add to panel
                panelToggles.Controls.Add(nud);
                // add matching label to the right

                var lbl = new Label
                {
                    Text = prop.Name,
                    AutoSize = true,
                    Location = new Point(nud.Width + 5, ypos)
                };
                panelToggles.Controls.Add(lbl);


                ypos += yInc;
            }



            // for every float - use a textbox and convvert to float
            foreach(var prop in settings.GetType().GetProperties().Where(p => p.PropertyType == typeof(float)))
            {
                var displayNameAttr = prop.GetCustomAttribute<MyDisplayNameAttrAttribute>();
                if (displayNameAttr == null) continue;
                // create a new textbox control
                var tb = new TextBox
                {
                    Text = (string)(prop.GetValue(settings).ToString()),
                    Location = new Point(0, ypos),
                    Width = 600
                };

                tb.TextChanged += (s, e) =>
                {
                    prop.SetValue(NewSettings, float.Parse(tb.Text));
                };

                // add to panel
                panelToggles.Controls.Add(tb);

                // add matching label to the right
                var lbl = new Label
                {
                    Text = prop.Name,
                    AutoSize = true,
                    Location = new Point(tb.Width + 5, ypos)
                };
                panelToggles.Controls.Add(lbl);

                ypos += yInc;
            }


        }

        private SettingsSet CloneSettings(SettingsSet settings)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(settings);
            return System.Text.Json.JsonSerializer.Deserialize<SettingsSet>(json);
        }

        private void InitializeDgvModels()
        {
            dgvModels.AllowUserToAddRows = true;
            dgvModels.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvModels.CellValueChanged += DgvModels_CellValueChanged;
            dgvModels.UserAddedRow += DgvModels_UserAddedRow;
            dgvModels.CellClick += DgvModels_CellClick;
        }

        private void CreateDgvColumns()
        {
            var columns = new[]
            {
            new { Name = "ApiName", HeaderText = "API Name", ReadOnly = false },
            new { Name = "ModelName", HeaderText = "Model Name", ReadOnly = false },
            new { Name = "ServiceName", HeaderText = "Service Name", ReadOnly = false },
            new { Name = "ModelUrl", HeaderText = "Model Url", ReadOnly = false },
            new { Name = "ModelKey", HeaderText = "Model Key", ReadOnly = false },
            new { Name = "ModelInputPrice", HeaderText = "Input 1MToken Price", ReadOnly = false },
            new { Name = "ModelOutputPrice", HeaderText = "Output 1MToken Price", ReadOnly = false },
            new { Name = "ModelColor", HeaderText = "Color", ReadOnly = false }
        };

            foreach (var col in columns)
            {
                dgvModels.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = col.Name,
                    HeaderText = col.HeaderText,
                    DataPropertyName = col.Name,
                    ReadOnly = col.ReadOnly
                });
            }

            dgvModels.Columns.Add(new DataGridViewButtonColumn
            {
                Name = "DeleteButton",
                HeaderText = "Delete",
                Text = "Delete",
                UseColumnTextForButtonValue = true
            });
        }

        private void CreateDgvRows(SettingsSet settings)
        {
            dgvModels.Rows.Clear();
            foreach (var api in settings.ApiList)
            {
                foreach (var model in api.Models)
                {
                    var index = dgvModels.Rows.Add(api.ApiName, model.ModelName, model.ServiceName, model.Url, model.Key, model.input1MTokenPrice, model.output1MTokenPrice, ColorTranslator.ToHtml(model.Color));
                    dgvModels.Rows[index].Cells["DeleteButton"].Value = "Delete";
                }
            }
        }

        private void DgvModels_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex == dgvModels.Columns["DeleteButton"].Index && e.RowIndex >= 0)
            {
                if (MessageBox.Show("Are you sure you want to delete this model?", "Confirm Delete", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    var row = dgvModels.Rows[e.RowIndex];
                    var apiName = row.Cells["ApiName"].Value?.ToString();
                    var modelName = row.Cells["ModelName"].Value?.ToString();

                    if (string.IsNullOrEmpty(apiName) || string.IsNullOrEmpty(modelName)) return;

                    var api = NewSettings.ApiList.FirstOrDefault(a => a.ApiName == apiName);
                    if (api != null)
                    {
                        api.Models.RemoveAll(m => m.ModelName == modelName);
                        if (api.Models.Count == 0)
                        {
                            NewSettings.ApiList.Remove(api);
                        }
                    }

                    dgvModels.Rows.RemoveAt(e.RowIndex);
                }
            }
        }

        private void DgvModels_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;

            var row = dgvModels.Rows[e.RowIndex];
            var apiName = row.Cells["ApiName"].Value?.ToString();
            var modelName = row.Cells["ModelName"].Value?.ToString();

            if (string.IsNullOrEmpty(apiName) || string.IsNullOrEmpty(modelName)) return;

            var api = NewSettings.ApiList.FirstOrDefault(a => a.ApiName == apiName);
            if (api == null)
            {
                api = new Api { ApiName = apiName, ApiUrl = "", Models = new List<Model>() };
                NewSettings.ApiList.Add(api);
            }

            var model = api.Models.FirstOrDefault(m => m.ModelName == modelName);
            if (model == null)
            {
                model = new Model();
                api.Models.Add(model);
            }

            model.ModelName = modelName;
            model.ServiceName = row.Cells["ServiceName"].Value?.ToString() ?? "";
            model.Url = row.Cells["ModelUrl"].Value?.ToString() ?? "";
            model.Key = row.Cells["ModelKey"].Value?.ToString() ?? "";
            decimal.TryParse(row.Cells["ModelInputPrice"].Value?.ToString(), out decimal inputPrice);
            model.input1MTokenPrice = inputPrice;
            decimal.TryParse(row.Cells["ModelOutputPrice"].Value?.ToString(), out decimal outputPrice);
            model.output1MTokenPrice = outputPrice;
            model.Color = ColorTranslator.FromHtml(row.Cells["ModelColor"].Value?.ToString() ?? "#FFFFFF");
        }

        private void DgvModels_UserAddedRow(object sender, DataGridViewRowEventArgs e)
        {
            // Set default values for the new row
            var row = e.Row;
            row.Cells["ApiName"].Value = "New API";
            row.Cells["ModelName"].Value = "New Model";
            row.Cells["ServiceName"].Value = "NewService";
            row.Cells["ModelUrl"].Value = "https://api.example.com";
            row.Cells["ModelKey"].Value = "";
            row.Cells["ModelInputPrice"].Value = 0;
            row.Cells["ModelOutputPrice"].Value = 0;
            row.Cells["ModelColor"].Value = "#FFFFFF";
        }

        private void btnSettingsCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            this.Close();
        }

        private void btnSettingsOK_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
