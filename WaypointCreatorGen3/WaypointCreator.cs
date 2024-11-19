﻿using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using System;

namespace WaypointCreatorGen3
{
    public partial class WaypointCreator : Form
    {
        // Dictionary<UInt32 /*CreatureID*/, Dictionary<UInt64 /*lowGUID*/, List<WaypointInfo>>>
        Dictionary<UInt32, Dictionary<UInt64, List<WaypointInfo>>> WaypointDatabyCreatureEntry = new Dictionary<UInt32, Dictionary<UInt64, List<WaypointInfo>>>();

        DataGridViewRow[] CopiedDataGridRows;

        public WaypointCreator()
        {
            InitializeComponent();
            GridViewContextMenuStrip.Enabled = false;
        }

        private void WaypointCreator_Load(object sender, EventArgs e)
        {

        }

        private async void EditorImportSniffButton_Click(object sender, EventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filter = "txt files (*.txt)|*.txt|All files (*.*)|*.*";
            dialog.FilterIndex = 1;
            dialog.RestoreDirectory = true;

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                EditorListBox.Items.Clear();
                EditorGridView.Rows.Clear();
                EditorWaypointChart.Series["Path"].Points.Clear();
                EditorWaypointChart.Series["Line"].Points.Clear();
                EditorImportSniffButton.Enabled = false;
                EditorFilterEntryButton.Enabled = false;
                GridViewContextMenuStrip.Enabled = false;
                EditorLoadingLabel.Text = "Loading [" + Path.GetFileName(dialog.FileName) + "]...";

                WaypointDatabyCreatureEntry = await Task.Run(() => GetWaypointDataFromSniff(dialog.FileName));

                EditorImportSniffButton.Enabled = true;
                EditorFilterEntryButton.Enabled = true;
                EditorLoadingLabel.Text = "Loaded [" + Path.GetFileName(dialog.FileName) + "].";
                ListEntries(0); // Initially listing all available GUIDs
            }
        }

        // Parses all waypoint data from the provided file and returns a container filled with all needed data
        private Dictionary<UInt32, Dictionary<UInt64, List<WaypointInfo>>> GetWaypointDataFromSniff(String filePath)
        {
            Dictionary<UInt32, Dictionary<UInt64, List<WaypointInfo>>> result = new Dictionary<UInt32, Dictionary<UInt64, List<WaypointInfo>>>();

            using (System.IO.StreamReader file = new System.IO.StreamReader(filePath))
            {
                string line;
                while ((line = file.ReadLine()) != null)
                {
                    if (line.Contains("SMSG_ON_MONSTER_MOVE") || line.Contains("SMSG_ON_MONSTER_MOVE_TRANSPORT"))
                    {
                        WaypointInfo wpInfo = new WaypointInfo();
                        UInt32 creatureId = 0;
                        UInt64 lowGuid = 0;

                        // Extracting the packet timestamp in milliseconds from the packet header for Delay calculations
                        string[] packetHeader = line.Split(' ');

                        for (int i = 0; i < packetHeader.Length; ++i)
                        {
                            if (packetHeader[i].Contains("Time:"))
                            {
                                wpInfo.TimeStamp = UInt32.Parse(TimeSpan.Parse(packetHeader[i + 2]).TotalMilliseconds.ToString());
                                break;
                            }
                        }

                        // Header noted, reading rest of the packet now
                        do
                        {
                            // Skip chase movement
                            if (line.Contains("Face:") && line.Contains("FacingTarget"))
                                break;

                            // Extracting entry and lowGuid from packet
                            if (line.Contains("MoverGUID:"))
                            {
                                string[] words = line.Split(' ');
                                for (int i = 0; i < words.Length; ++i)
                                {
                                    if (words[i].Contains("Entry:"))
                                        creatureId = UInt32.Parse(words[i + 1]);
                                    else if (words[i].Contains("Low:"))
                                        lowGuid = UInt64.Parse(words[i + 1]);
                                }

                                // Skip invalid data.
                                if (creatureId == 0 || lowGuid == 0)
                                    break;
                            }

                            // Extracting spline duration
                            if (line.Contains("MoveTime:"))
                            {
                                string[] words = line.Split(' ');
                                for (int i = 0; i < words.Length; ++i)
                                    if (words[i].Contains("MoveTime:"))
                                        wpInfo.MoveTime = UInt32.Parse(words[i + 1]);
                            }

                            // Extract Facing Angles
                            if (line.Contains("FaceDirection:"))
                            {
                                string[] words = line.Split(' ');
                                for (int i = 0; i < words.Length; ++i)
                                    if (words[i].Contains("FaceDirection:"))
                                        wpInfo.Position.Orientation = float.Parse(words[i + 1], CultureInfo.InvariantCulture);
                            }

                            // Extracting waypoint (The space in the string is intentional. Do not remove!)
                            if (line.Contains(" Points:"))
                            {
                                string[] words = line.Split(' ');
                                for (int i = 0; i < words.Length; ++i)
                                {
                                    if (words[i].Contains("X:"))
                                        wpInfo.Position.PositionX = float.Parse(words[i + 1], CultureInfo.InvariantCulture);
                                    else if (words[i].Contains("Y:"))
                                        wpInfo.Position.PositionY = float.Parse(words[i + 1], CultureInfo.InvariantCulture);
                                    else if (words[i].Contains("Z:"))
                                        wpInfo.Position.PositionZ = float.Parse(words[i + 1], CultureInfo.InvariantCulture);
                                }

                                // Delay Calculation
                                if (result.ContainsKey(creatureId) && result[creatureId].ContainsKey(lowGuid))
                                {
                                    if (result[creatureId][lowGuid].Count != 0)
                                    {
                                        int index = result[creatureId][lowGuid].Count - 1;
                                        Int64 timeDiff = wpInfo.TimeStamp - result[creatureId][lowGuid][index].TimeStamp;
                                        UInt32 oldMoveTime = result[creatureId][lowGuid][index].MoveTime;
                                        result[creatureId][lowGuid][index].Delay = Convert.ToInt32(timeDiff - oldMoveTime);
                                    }
                                }

                                // Everything gathered, time to store the data
                                if (!result.ContainsKey(creatureId))
                                    result.Add(creatureId, new Dictionary<UInt64, List<WaypointInfo>>());

                                if (!result[creatureId].ContainsKey(lowGuid))
                                    result[creatureId].Add(lowGuid, new List<WaypointInfo>());

                                result[creatureId][lowGuid].Add(wpInfo);
                            }

                            if (line.Contains(" WayPoints:"))
                            {
                                string[] words = line.Split(' ');
                                SplinePosition splinePosition = new SplinePosition();
                                for (int i = 0; i < words.Length; ++i)
                                {
                                    if (words[i].Contains("X:"))
                                        splinePosition.PositionX = float.Parse(words[i + 1], CultureInfo.InvariantCulture);
                                    else if (words[i].Contains("Y:"))
                                        splinePosition.PositionY = float.Parse(words[i + 1], CultureInfo.InvariantCulture);
                                    else if (words[i].Contains("Z:"))
                                        splinePosition.PositionZ = float.Parse(words[i + 1], CultureInfo.InvariantCulture);
                                }

                                wpInfo.SplineList.Add(splinePosition);
                            }
                        }
                        while ((line = file.ReadLine()) != "");
                    }
                }
            }

            return result;
        }

        private void ListEntries(UInt32 creatureId)
        {
            EditorListBox.Items.Clear();

            if (creatureId == 0)
            {

                foreach (var waypointsByEntry in WaypointDatabyCreatureEntry)
                    foreach (var waypointsByGuid in waypointsByEntry.Value)
                        EditorListBox.Items.Add(waypointsByEntry.Key.ToString() + " (" + waypointsByGuid.Key.ToString() + ")");
            }
            else
            {
                if (WaypointDatabyCreatureEntry.ContainsKey(creatureId))
                    foreach (var waypointsByGuid in WaypointDatabyCreatureEntry[creatureId])
                        EditorListBox.Items.Add(creatureId.ToString() + " (" + waypointsByGuid.Key.ToString() + ")");
            }
        }

        private void ShowWaypointDataForCreature(UInt32 creatureId, UInt64 lowGUID)
        {
            // Filling the GridView
            EditorGridView.Rows.Clear();
            SplineGridView.Rows.Clear();

            if (!WaypointDatabyCreatureEntry.ContainsKey(creatureId))
                return;

            if (WaypointDatabyCreatureEntry[creatureId].ContainsKey(lowGUID))
            {
                int count = 0;
                foreach (WaypointInfo wpInfo in WaypointDatabyCreatureEntry[creatureId][lowGUID])
                {
                    int splineCount = 0;
                    string Orientation = "NULL";
                    if (wpInfo.Position.Orientation != 0f)
                        Orientation = wpInfo.Position.Orientation.ToString(CultureInfo.InvariantCulture);

                    EditorGridView.Rows.Add(
                        count,
                        wpInfo.Position.PositionX.ToString(CultureInfo.InvariantCulture),
                        wpInfo.Position.PositionY.ToString(CultureInfo.InvariantCulture),
                        wpInfo.Position.PositionZ.ToString(CultureInfo.InvariantCulture),
                        Orientation,
                        wpInfo.MoveTime,
                        wpInfo.Delay);

                    foreach (SplinePosition splineInfo in wpInfo.SplineList)
                    {
                        SplineGridView.Rows.Add(
                            count,
                            splineCount,
                            splineInfo.PositionX.ToString(CultureInfo.InvariantCulture),
                            splineInfo.PositionY.ToString(CultureInfo.InvariantCulture),
                            splineInfo.PositionZ.ToString(CultureInfo.InvariantCulture));

                        ++splineCount;
                    }

                    ++count;
                }
            }

            BuildGraphPath();
            GridViewContextMenuStrip.Enabled = true;
        }

        private void BuildGraphPath()
        {
            EditorWaypointChart.ChartAreas[0].AxisX.ScaleView.ZoomReset();
            EditorWaypointChart.ChartAreas[0].AxisY.ScaleView.ZoomReset();

            EditorWaypointChart.Series["Path"].Points.Clear();
            EditorWaypointChart.Series["Line"].Points.Clear();

            foreach (DataGridViewRow dataRow in EditorGridView.Rows)
            {
                float x = float.Parse(dataRow.Cells[1].Value.ToString(), CultureInfo.InvariantCulture);
                float y = float.Parse(dataRow.Cells[2].Value.ToString(), CultureInfo.InvariantCulture);

                EditorWaypointChart.Series["Path"].Points.AddXY(x, y);
                EditorWaypointChart.Series["Path"].Points[dataRow.Index].Label = dataRow.Index.ToString();
                EditorWaypointChart.Series["Line"].Points.AddXY(x, y);
            }
        }

        // Filters the ListBox entries by CreatureID
        private void EditorFilterEntryButton_Click(object sender, EventArgs e)
        {
            UInt32 creatureId = 0;
            UInt32.TryParse(EditorFilterEntryTextBox.Text, out creatureId);
            ListEntries(creatureId);
        }

        private void EditorListBox_SelectedValueChanged(object sender, EventArgs e)
        {
            if (EditorListBox.SelectedIndex == -1)
                return;

            string[] words = EditorListBox.SelectedItem.ToString().Replace("(", "").Replace(")", "").Split(new char[] { ' ' });

            UInt32 creatureId = UInt32.Parse(words[0]);
            UInt64 lowGuid = UInt64.Parse(words[1]);
            ShowWaypointDataForCreature(creatureId, lowGuid);
        }

        private void CutStripMenuItem_Click(object sender, EventArgs e)
        {
            if (EditorGridView.SelectedRows.Count == 0)
                return;

            CopiedDataGridRows = new DataGridViewRow[EditorGridView.SelectedRows.Count];
            EditorGridView.SelectedRows.CopyTo(CopiedDataGridRows, 0);

            foreach (DataGridViewRow row in EditorGridView.SelectedRows)
                EditorGridView.Rows.Remove(row);

            // Update the row count field
            int count = 0;
            foreach (DataGridViewRow row in EditorGridView.Rows)
            {
                row.Cells[0].Value = count;
                ++count;
            }

            // GriwView is updated, rebuild the graph path.
            BuildGraphPath();
        }

        private void CopyStripMenuItem_Click(object sender, EventArgs e)
        {
            if (EditorGridView.SelectedRows.Count == 0)
                return;

            CopiedDataGridRows = new DataGridViewRow[EditorGridView.SelectedRows.Count];
            EditorGridView.SelectedRows.CopyTo(CopiedDataGridRows, 0);
        }

        private void PasteAboveStripMenuItem_Click(object sender, EventArgs e)
        {
            PasteRows(true);
        }

        private void PasteBelowStripMenuItem_Click(object sender, EventArgs e)
        {
            PasteRows(false);
        }

        private void PasteRows(bool aboveSelection)
        {
            if (CopiedDataGridRows == null || CopiedDataGridRows.Length == 0 || EditorGridView.SelectedRows.Count == 0)
                return;

            int index = aboveSelection ? EditorGridView.SelectedRows[0].Index : EditorGridView.SelectedRows[EditorGridView.SelectedRows.Count - 1].Index + 1;

            DataGridViewRow[] rowsCopy = new DataGridViewRow[EditorGridView.Rows.Count];
            EditorGridView.Rows.CopyTo(rowsCopy, 0);
            EditorGridView.Rows.Clear();
            Array.Reverse(CopiedDataGridRows);

            int appendedIndex = 0;
            // First we append all waypoints that we had before the paste location
            for (; appendedIndex < index; ++appendedIndex)
                EditorGridView.Rows.Add(
                    appendedIndex,
                    rowsCopy[appendedIndex].Cells[1].Value,
                    rowsCopy[appendedIndex].Cells[2].Value,
                    rowsCopy[appendedIndex].Cells[3].Value,
                    rowsCopy[appendedIndex].Cells[4].Value,
                    rowsCopy[appendedIndex].Cells[5].Value,
                    rowsCopy[appendedIndex].Cells[6].Value);

            // Paste location reached, append copied rows
            foreach (DataGridViewRow row in CopiedDataGridRows)
                EditorGridView.Rows.Add(
                    index++,
                    row.Cells[1].Value,
                    row.Cells[2].Value,
                    row.Cells[3].Value,
                    row.Cells[4].Value,
                    row.Cells[5].Value,
                    row.Cells[6].Value);

            // Copied rows added, append remaining points
            for (; appendedIndex < rowsCopy.Length; ++appendedIndex)
                EditorGridView.Rows.Add(
                    appendedIndex + CopiedDataGridRows.Length,
                    rowsCopy[appendedIndex].Cells[1].Value,
                    rowsCopy[appendedIndex].Cells[2].Value,
                    rowsCopy[appendedIndex].Cells[3].Value,
                    rowsCopy[appendedIndex].Cells[4].Value,
                    rowsCopy[appendedIndex].Cells[5].Value,
                    rowsCopy[appendedIndex].Cells[6].Value);

            // GridView is updated, rebuild the graph path.
            BuildGraphPath();
        }

        private void GenerateSQLStripMenuItem_Click(object sender, EventArgs e)
        {
            // Generates the SQL output.
            // waypoint_path_node
            SQLOutputTextBox.AppendText("SET @ACGUID := xxxxxx;\r\n");
            SQLOutputTextBox.AppendText("SET @PATH := @ACGUID * 10;\r\n");
            SQLOutputTextBox.AppendText("DELETE FROM `waypoint_path_node` WHERE `PathId`= @PATH;\r\n");
            SQLOutputTextBox.AppendText("INSERT INTO `waypoint_path_node` (`PathId`, `NodeId`, `PositionX`, `PositionY`, `PositionZ`, `Orientation`, `Delay`) VALUES\r\n");

            int rowCount = 0;
            DataGridViewRow firstRow = null;
            foreach (DataGridViewRow row in EditorGridView.Rows)
            {
                if (rowCount == 0)
                    firstRow = row;

                ++rowCount;
                if (rowCount < EditorGridView.Rows.Count)
                    SQLOutputTextBox.AppendText($"(@PATH, {row.Cells[0].Value}, {row.Cells[1].Value}, {row.Cells[2].Value}, {row.Cells[3].Value}, {row.Cells[4].Value}, {row.Cells[6].Value}),\r\n");
                else
                    SQLOutputTextBox.AppendText($"(@PATH, {row.Cells[0].Value}, {row.Cells[1].Value}, {row.Cells[2].Value}, {row.Cells[3].Value}, {row.Cells[4].Value}, {row.Cells[6].Value});\r\n");
            }

            SQLOutputTextBox.AppendText("\r\n");

            // creature
            if (firstRow != null)
            {
                SQLOutputTextBox.AppendText($"UPDATE `creature` SET `position_x`= {firstRow.Cells[1].Value}, `position_y`= {firstRow.Cells[2].Value}, `position_z`= {firstRow.Cells[3].Value}, `orientation`= {firstRow.Cells[4].Value} WHERE `guid`= @ACGUID;\r\n");
            }

            SQLOutputTextBox.AppendText($"UPDATE `creature` SET `MovementType`= 2 WHERE `guid`= @ACGUID;\r\n");
            SQLOutputTextBox.AppendText("\r\n");

            SQLOutputTextBox.AppendText($"UPDATE `creature_template` SET `AIName` = 'SmartAI', `ScriptName` = '' WHERE `entry` = xxxxx;\r\n");
            SQLOutputTextBox.AppendText("\r\n");

            // creature_addon
            SQLOutputTextBox.AppendText("DELETE FROM `creature_addon` WHERE `guid`= @ACGUID;\r\n");
            SQLOutputTextBox.AppendText("INSERT INTO `creature_addon` (`guid`, `PathId`, `mount`, `StandState`, `SheathState`, `emote`, `visibilityDistanceType`, `auras`) VALUES\r\n");
            SQLOutputTextBox.AppendText("(@ACGUID, @PATH, 0, 0, 1, 0, 0, '');\r\n");
            SQLOutputTextBox.AppendText("\r\n");

            SQLOutputTextBox.AppendText("DELETE FROM `waypoint_path` WHERE `PathId`=@PATH;\r\n");
            SQLOutputTextBox.AppendText("INSERT INTO `waypoint_path` (`PathId`, `MoveType`, `Flags`, `Velocity`, `Comment`) VALUES\r\n");
            string orientationStr = "NULL";
            if (EditorGridView.Rows.Count > 0)
            {
                var wpInfo = EditorGridView.Rows[0].DataBoundItem as WaypointInfo;
                if (wpInfo != null && wpInfo.Position.Orientation != 0f)
                    orientationStr = wpInfo.Position.Orientation.ToString(CultureInfo.InvariantCulture);
            }
            SQLOutputTextBox.AppendText("(@PATH, 0, 0, null, 'xxxx - xxxx');\r\n");
            SQLOutputTextBox.AppendText("\r\n");

            TabControl.SelectedTab = TabControl.TabPages[1];
        }

        private void SQLOutputSaveButton_Click(object sender, EventArgs e)
        {
            // Saving the text of the SQLOutputTextBox into a file
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Filter = "Structured Query Language (*.sql)|*.sql|All files (*.*)|*.*";
            dialog.FilterIndex = 1;
            dialog.DefaultExt = "sql";
            dialog.RestoreDirectory = true;

            if (dialog.ShowDialog() == DialogResult.OK)
                File.WriteAllText(dialog.FileName, SQLOutputTextBox.Text, System.Text.Encoding.UTF8);
        }
    }

    public class WaypointInfo
    {
        public WaypointPosition Position { get; set; } = new WaypointPosition();

        public UInt32 TimeStamp = 0;
        public UInt32 MoveTime = 0;
        public Int32 Delay = 0;
        public List<SplinePosition> SplineList = new List<SplinePosition>();
    }

    public class WaypointPosition
    {
        public float PositionX = 0f;
        public float PositionY = 0f;
        public float PositionZ = 0f;
        public float Orientation = 0f;
    }

    public class SplinePosition
    {
        public float PositionX = 0f;
        public float PositionY = 0f;
        public float PositionZ = 0f;
    }

}
