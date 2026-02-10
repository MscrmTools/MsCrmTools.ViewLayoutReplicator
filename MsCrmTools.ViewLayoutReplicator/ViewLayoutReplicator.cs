// PROJECT : MsCrmTools.ViewLayoutReplicator
// This project was developed by Tanguy Touzard
// CODEPLEX: http://xrmtoolbox.codeplex.com
// BLOG: http://mscrmtools.blogspot.com

using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using MsCrmTools.ViewLayoutReplicator.Forms;
using MsCrmTools.ViewLayoutReplicator.Helpers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using Tanguy.WinForm.Utilities.DelegatesHelpers;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;

namespace MsCrmTools.ViewLayoutReplicator
{
    public partial class ViewLayoutReplicator : PluginControlBase, IGitHubPlugin, IHelpPlugin
    {
        private List<EntityMetadata> entitiesCache;
        private ListViewItem[] listViewItemsCache;
        private Guid solutionId = Guid.Empty;
        private List<ListViewItem> sourceViewsItems;
        private List<ListViewItem> targetViewsItems;

        #region Constructor

        public ViewLayoutReplicator()
        {
            InitializeComponent();

            var tt = new ToolTip();
            tt.SetToolTip(lvSourceViews, "Double click on a selected row to display its layout XML");
        }

        #endregion Constructor

        #region Main ToolStrip Handlers

        #region Fill Entities

        private void LoadEntities(bool fromSolution = false)
        {
            if (fromSolution)
            {
                using (var dialog = new SolutionPicker(Service))
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        solutionId = dialog.SelectedSolution.FirstOrDefault()?.Id ?? Guid.Empty;
                    }
                    else
                    {
                        return;
                    }
                }
            }
            else
            {
                solutionId = Guid.Empty;
            }

            txtSearchEntity.Text = string.Empty;
            lvEntities.Items.Clear();
            gbEntities.Enabled = false;
            tsbPublishEntity.Enabled = false;
            tsbPublishAll.Enabled = false;
            tsbSaveViews.Enabled = false;
            tsbSaveAndPublish.Enabled = false;

            lvSourceViews.Items.Clear();
            lvTargetViews.Items.Clear();
            lvSourceViewLayoutPreview.Columns.Clear();

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Loading entities...",
                Work = (bw, e) =>
                {
                    e.Result = MetadataHelper.RetrieveEntities(Service, solutionId);
                },
                PostWorkCallBack = e =>
                {
                    if (e.Error != null)
                    {
                        string errorMessage = CrmExceptionHelper.GetErrorMessage(e.Error, true);
                        CommonDelegates.DisplayMessageBox(ParentForm, errorMessage, "Error", MessageBoxButtons.OK,
                                                          MessageBoxIcon.Error);
                    }
                    else
                    {
                        entitiesCache = (List<EntityMetadata>)e.Result;
                        lvEntities.Items.Clear();
                        var list = new List<ListViewItem>();
                        foreach (EntityMetadata emd in (List<EntityMetadata>)e.Result)
                        {
                            var item = new ListViewItem { Text = emd.DisplayName.UserLocalizedLabel?.Label ?? "N/A", Tag = emd };
                            item.SubItems.Add(emd.LogicalName);
                            list.Add(item);
                        }

                        this.listViewItemsCache = list.ToArray();
                        lvEntities.Items.AddRange(listViewItemsCache);

                        gbEntities.Enabled = true;
                        tsbPublishEntity.Enabled = true;
                        tsbPublishAll.Enabled = true;
                        tsbSaveViews.Enabled = true;

                        btnSelectFromSolution.Visible = fromSolution;
                    }
                }
            });
        }

        #endregion Fill Entities

        #region Save Views

        private void SaveViews(bool publish = false)
        {
            if (lvSourceViews.SelectedItems.Count == 0) return;

            var targetViews = lvTargetViews.CheckedItems.Cast<ListViewItem>().Select(i => new ViewDefinition((Entity)i.Tag)).ToList();
            var sourceView = new ViewDefinition((Entity)lvSourceViews.SelectedItems.Cast<ListViewItem>().First().Tag);

            if (sourceView.LayoutXml.Contains(".")
                && targetViews.Any(tv => tv.Type == ViewHelper.VIEW_QUICKFIND)
                && new Version(ConnectionDetail.OrganizationVersion) >= new Version(8, 2, 0, 0)
                && new Version(ConnectionDetail.OrganizationVersion) < new Version(9, 1, 0, 0))
            {
                var message = "The source view contains related table attribute and you selected the Quick Search view as a target. This is not allowed in Microsoft Dynamics 365";
                MessageBox.Show(this, message, "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("The selected target views will:");
            sb.AppendLine($"- {(chkCopyLayout.Checked ? "" : "NOT ")}be updated with the layout of the source view");
            sb.AppendLine($"- {(chkCopySortOrder.Checked ? "" : "NOT ")}be updated with the sort order of the source view");
            sb.AppendLine($"- {(chkCopyComponentsConfig.Checked ? "" : "NOT ")}be updated with components configuration");
            sb.AppendLine();
            sb.AppendLine("Are your sure you want to update all selected view(s) ?");
            if (DialogResult.No ==
                MessageBox.Show(this, sb.ToString(), "Question", MessageBoxButtons.YesNo, MessageBoxIcon.Question))
            {
                return;
            }

            tsbPublishEntity.Enabled = false;
            tsbPublishAll.Enabled = false;
            tsbSaveViews.Enabled = false;
            tssbLoadTables.Enabled = false;
            tsbSaveAndPublish.Enabled = false;

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Saving views...",
                AsyncArgument = new object[] { sourceView, targetViews, chkCopyLayout.Checked, chkCopySortOrder.Checked, chkCopyComponentsConfig.Checked },
                Work = (bw, evt) =>
                {
                    var args = (object[])evt.Argument;
                    evt.Result = ViewHelper.PropagateLayout((ViewDefinition)args[0], (List<ViewDefinition>)args[1], (bool)args[2], (bool)args[3], (bool)args[4], Service);
                },
                PostWorkCallBack = evt =>
                {
                    if (((List<Tuple<string, string>>)evt.Result).Count > 0)
                    {
                        var errorDialog = new ErrorList((List<Tuple<string, string>>)evt.Result);
                        errorDialog.ShowDialog(this);
                    }
                    else if (publish)
                    {
                        TsbPublishEntityClick(tsbPublishEntity, new EventArgs());
                    }

                    tsbPublishEntity.Enabled = true;
                    tsbPublishAll.Enabled = true;
                    tsbSaveViews.Enabled = true;
                    tssbLoadTables.Enabled = true;
                    tsbSaveAndPublish.Enabled = true;
                }
            });
        }

        private void TsbSaveViewsClick(object sender, EventArgs e)
        {
            SaveViews();
        }

        #endregion Save Views

        #region Publish Entity

        private void TsbPublishEntityClick(object sender, EventArgs e)
        {
            if (lvEntities.SelectedItems.Count > 0)
            {
                tsbPublishEntity.Enabled = false;
                tsbPublishAll.Enabled = false;
                tsbSaveViews.Enabled = false;
                tssbLoadTables.Enabled = false;
                tsbSaveAndPublish.Enabled = false;

                WorkAsync(new WorkAsyncInfo
                {
                    Message = "Publishing table...",
                    AsyncArgument = lvEntities.SelectedItems[0].Tag,
                    Work = (bw, evt) =>
                    {
                        var pubRequest = new PublishXmlRequest();
                        pubRequest.ParameterXml = string.Format(@"<importexportxml>
                                                           <entities>
                                                              <entity>{0}</entity>
                                                           </entities>
                                                           <nodes/><securityroles/><settings/><workflows/>
                                                        </importexportxml>",
                                                                evt.Argument);

                        Service.Execute(pubRequest);
                    },
                    PostWorkCallBack = evt =>
                    {
                        if (evt.Error != null)
                        {
                            string errorMessage = CrmExceptionHelper.GetErrorMessage(evt.Error, false);
                            MessageBox.Show(this, errorMessage, "Error", MessageBoxButtons.OK,
                                                              MessageBoxIcon.Error);
                        }

                        tsbPublishEntity.Enabled = true;
                        tsbPublishAll.Enabled = true;
                        tsbSaveViews.Enabled = true;
                        tssbLoadTables.Enabled = true;
                        tsbSaveAndPublish.Enabled = true;
                    }
                });
            }
        }

        #endregion Publish Entity

        #endregion Main ToolStrip Handlers

        #region ListViews Handlers

        #region Fill Views

        private void BwFillViewsDoWork(object sender, DoWorkEventArgs e)
        {
            var emd = (EntityMetadata)e.Argument;

            List<Entity> viewsList = ViewHelper.RetrieveViews(emd.LogicalName, entitiesCache, Service);
            viewsList.AddRange(ViewHelper.RetrieveUserViews(emd.LogicalName, entitiesCache, Service));

            sourceViewsItems = new List<ListViewItem>();
            targetViewsItems = new List<ListViewItem>();

            foreach (Entity view in viewsList)
            {
                bool display = true;

                var item = new ListViewItem(view["name"].ToString());
                item.Tag = view;

                #region Gestion de l'image associée à la vue

                switch ((int)view["querytype"])
                {
                    case ViewHelper.VIEW_BASIC:
                        {
                            if (view.LogicalName == "savedquery")
                            {
                                if ((bool)view["isdefault"])
                                {
                                    item.SubItems.Add("Default public view");
                                    item.ImageIndex = 3;
                                }
                                else
                                {
                                    item.SubItems.Add("Public view");
                                    item.ImageIndex = 0;
                                }
                            }
                            else
                            {
                                item.SubItems.Add("User view");
                                item.ImageIndex = 6;
                            }
                        }
                        break;

                    case ViewHelper.VIEW_ADVANCEDFIND:
                        {
                            item.SubItems.Add("Advanced find view");
                            item.ImageIndex = 1;
                        }
                        break;

                    case ViewHelper.VIEW_ASSOCIATED:
                        {
                            item.SubItems.Add("Associated view");
                            item.ImageIndex = 2;
                        }
                        break;

                    case ViewHelper.VIEW_QUICKFIND:
                        {
                            item.SubItems.Add("QuickFind view");
                            item.ImageIndex = 5;
                        }
                        break;

                    case ViewHelper.VIEW_SEARCH:
                        {
                            item.SubItems.Add("Lookup view");
                            item.ImageIndex = 4;
                        }
                        break;

                    default:
                        {
                            //item.SubItems.Add(view["name"].ToString());
                            display = false;
                        }
                        break;
                }

                #endregion Gestion de l'image associée à la vue

                if (display)
                {
                    // Add view to each list of views (source and target)
                    ListViewItem clonedItem = (ListViewItem)item.Clone();

                    sourceViewsItems.Add(item);

                    if (view.Contains("iscustomizable") && ((BooleanManagedProperty)view["iscustomizable"]).Value == false
                        && view.Contains("ismanaged") && (bool)view["ismanaged"])
                    {
                        clonedItem.ForeColor = Color.Gray;
                        clonedItem.ToolTipText = "This managed view has not been defined as customizable";
                    }

                    targetViewsItems.Add(clonedItem);
                }

                var component = Service.RetrieveMultiple(new QueryExpression("solutioncomponent")
                {
                    ColumnSet = new ColumnSet("objectid", "rootcomponentbehavior"),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("componenttype", ConditionOperator.Equal, 1), // Table
                            new ConditionExpression("objectid", ConditionOperator.Equal, emd.MetadataId.Value),
                            new ConditionExpression("solutionid", ConditionOperator.Equal, solutionId)
                        }
                    },
                }).Entities.FirstOrDefault();

                e.Result = component?.GetAttributeValue<OptionSetValue>("rootcomponentbehavior").Value != 0;
            }
        }

        private void BwFillViewsRunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            Cursor = Cursors.Default;
            gbSourceViews.Enabled = true;
            gbTargetViews.Enabled = true;

            if (e.Error != null)
            {
                MessageBox.Show(this, "An error occured: " + e.Error.Message, "Error", MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
                return;
            }

            if (sourceViewsItems.Count == 0)
            {
                MessageBox.Show(this, "This table does not contain any view", "Warning", MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
                return;
            }

            lvSourceViews.Items.AddRange(sourceViewsItems.ToArray());
            lvTargetViews.Items.AddRange(targetViewsItems.ToArray());

            btnSelectFromSolution.Visible = (bool)e.Result;
        }

        private void lvEntities_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lvEntities.SelectedItems.Count > 0)
            {
                var emd = (EntityMetadata)lvEntities.SelectedItems[0].Tag;

                // Reinit other controls
                lvSourceViews.Items.Clear();
                lvSourceViewLayoutPreview.Columns.Clear();
                lvTargetViews.Items.Clear();

                Cursor = Cursors.WaitCursor;

                // Launch treatment
                var bwFillViews = new BackgroundWorker();
                bwFillViews.DoWork += BwFillViewsDoWork;
                bwFillViews.RunWorkerAsync(emd);
                bwFillViews.RunWorkerCompleted += BwFillViewsRunWorkerCompleted;
            }
        }

        #endregion Fill Views

        #region Display View

        private void LvSourceViewsSelectedIndexChanged(object sender, EventArgs e)
        {
            lvSourceViewLayoutPreview.Columns.Clear();

            if (lvSourceViews.SelectedItems.Count > 0)
            {
                lvSourceViews.SelectedIndexChanged -= LvSourceViewsSelectedIndexChanged;
                lvSourceViewLayoutPreview.Items.Clear();
                lvSourceViews.Enabled = false;

                WorkAsync(new WorkAsyncInfo
                {
                    Message = "Loading view layout...",
                    AsyncArgument = lvSourceViews.SelectedItems[0].Tag,
                    Work = (bw, evt) =>
                    {
                        Entity currentSelectedView = (Entity)evt.Argument;
                        string layoutXml = currentSelectedView["layoutxml"].ToString();
                        string fetchXml = currentSelectedView.Contains("fetchxml")
                                              ? currentSelectedView["fetchxml"].ToString()
                                              : string.Empty;
                        var selectedItem = ListViewDelegates.GetSelectedItems(lvEntities)[0];
                        string currentEntityLogicalName = selectedItem.SubItems[1].Text;
                        EntityMetadata currentEmd = entitiesCache.Find(emd => emd.LogicalName == currentEntityLogicalName);

                        XmlDocument layoutDoc = new XmlDocument();
                        layoutDoc.LoadXml(layoutXml);

                        XmlDocument fetchDoc = new XmlDocument();
                        fetchDoc.LoadXml(fetchXml);

                        EntityMetadata emdWithItems = MetadataHelper.RetrieveEntity(currentEmd.LogicalName, Service);

                        var headers = new List<ColumnHeader>();

                        var item = new ListViewItem();

                        string orderColumn = "";
                        bool orderDirection = false;
                        var orderNode = fetchDoc.SelectSingleNode("fetch/entity/order");
                        if (orderNode != null)
                        {
                            orderColumn = orderNode.Attributes["attribute"].Value;
                            orderDirection = orderNode.Attributes["descending"]?.Value?.ToLower() == "false";
                        }

                        foreach (XmlNode columnNode in layoutDoc.SelectNodes("grid/row/cell"))
                        {
                            ColumnHeader header = new ColumnHeader();

                            header.Width = columnNode.Attributes["width"] == null ? 150 : int.Parse(columnNode.Attributes["width"].Value);
                            header.Text = MetadataHelper.RetrieveAttributeDisplayName(emdWithItems,
                                                                                      columnNode.Attributes["name"].Value,
                                                                                      fetchXml, Service);

                            if (columnNode.Attributes["name"].Value == orderColumn)
                            {
                                header.Text += (orderDirection ? " ↑" : " ↓");
                            }

                            headers.Add(header);

                            if (string.IsNullOrEmpty(item.Text))
                                item.Text = header.Width + "px";
                            else
                                item.SubItems.Add(header.Width + "px");
                        }

                        evt.Result = new object[] { headers, item };
                    },
                    PostWorkCallBack = evt =>
                    {
                        if (evt.Error != null)
                        {
                            MessageBox.Show(ParentForm, "Error while displaying view: " + evt.Error.Message, "Error",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                        else
                        {
                            var args = (object[])evt.Result;

                            lvSourceViewLayoutPreview.Columns.AddRange(((List<ColumnHeader>)args[0]).ToArray());
                            lvSourceViewLayoutPreview.Items.Add((ListViewItem)args[1]);
                            lvSourceViewLayoutPreview.Enabled = true;

                            foreach (ListViewItem item in lvTargetViews.Items)
                            {
                                item.Checked = (((Entity)item.Tag).LogicalName == "savedquery" && ((Entity)item.Tag).GetAttributeValue<BooleanManagedProperty>("iscustomizable").Value || ((Entity)item.Tag).LogicalName == "userquery") && ((Entity)item.Tag).Id != ((Entity)lvSourceViews.SelectedItems[0].Tag).Id;
                            }
                        }

                        lvSourceViews.SelectedIndexChanged += LvSourceViewsSelectedIndexChanged;
                        lvSourceViews.Enabled = true;
                    }
                });
            }
        }

        private void LvTargetViewsItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (e.Item.Checked && e.Item.ForeColor == Color.Gray)
            {
                MessageBox.Show(this, "This view has not been defined as customizable. It can't be customized!",
                                "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                e.Item.Checked = false;
            }

            if (ListViewDelegates.GetCheckedItems(lvTargetViews).Length > 0)
            {
                tsbSaveViews.Enabled = true;
                tsbPublishEntity.Enabled = true;
                tsbSaveAndPublish.Enabled = true;
            }
            else
            {
                tsbSaveViews.Enabled = false;
                tsbPublishEntity.Enabled = false;
                tsbSaveAndPublish.Enabled = false;
            }
        }

        #endregion Display View

        #endregion ListViews Handlers

        public string HelpUrl
        { get { return "https://github.com/MscrmTools/MsCrmTools.ViewLayoutReplicator/wiki"; } }

        public string RepositoryName
        { get { return "MscrmTools.ViewLayoutReplicator"; } }

        public string UserName
        { get { return "MscrmTools"; } }

        private void btnSelectFromSolution_Click(object sender, EventArgs e)
        {
            var table = ((EntityMetadata)lvEntities.SelectedItems[0].Tag).LogicalName;

            var components = Service.RetrieveMultiple(new QueryExpression("solutioncomponent")
            {
                ColumnSet = new ColumnSet("objectid"),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("componenttype", ConditionOperator.Equal, 26), // View
                        new ConditionExpression("objectid", ConditionOperator.In, lvTargetViews.Items.Cast<ListViewItem>().Select(i =>  ((Entity)i.Tag).Id).ToArray()),
                        new ConditionExpression("solutionid", ConditionOperator.Equal, solutionId)
                    }
                },
            }).Entities.ToList();

            var ids = components.Select(c => c.GetAttributeValue<Guid>("objectid")).ToList();

            foreach (ListViewItem item in lvTargetViews.Items)
            {
                item.Checked = ids.Contains(((Entity)item.Tag).Id);
            }
        }

        private void chkShowSystem_CheckedChanged(object sender, EventArgs e)
        {
            FilterTargetViews(chkShowSystem.Checked, chkShowUser.Checked);
        }

        private void chkShowUser_CheckedChanged(object sender, EventArgs e)
        {
            FilterTargetViews(chkShowSystem.Checked, chkShowUser.Checked);
        }

        private void FilterTargetViews(bool showSystem, bool showUser)
        {
            var filteredViews = targetViewsItems.Where(v =>
                ((Entity)v.Tag).LogicalName == "savedquery" && showSystem
                || ((Entity)v.Tag).LogicalName == "userquery" && showUser
                );

            lvTargetViews.Items.Clear();

            lvTargetViews.Items.AddRange(filteredViews.ToArray());
        }

        private void llClearSelection_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            foreach (ListViewItem item in lvTargetViews.Items)
            {
                item.Checked = false;
            }
        }

        private void LvEntitiesColumnClick(object sender, ColumnClickEventArgs e)
        {
            lvEntities.Sorting = lvEntities.Sorting == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
            lvEntities.ListViewItemSorter = new ListViewItemComparer(e.Column, lvEntities.Sorting);
        }

        private void LvSourceViewsDoubleClick(object sender, EventArgs e)
        {
            if (lvSourceViews.SelectedItems.Count == 0)
                return;

            ListViewItem item = lvSourceViews.SelectedItems[0];
            var view = (Entity)item.Tag;

            var dialog = new XmlContentDisplayDialog(view["layoutxml"].ToString());
            dialog.ShowDialog(this);
        }

        private void OnSearchKeyUp(object sender, KeyEventArgs e)
        {
            var entityName = txtSearchEntity.Text;
            if (string.IsNullOrWhiteSpace(entityName))
            {
                lvEntities.BeginUpdate();
                lvEntities.Items.Clear();
                lvEntities.Items.AddRange(listViewItemsCache);
                lvEntities.EndUpdate();
            }
            else
            {
                lvEntities.BeginUpdate();
                lvEntities.Items.Clear();
                var filteredItems = listViewItemsCache
                    .Where(i => i.SubItems.Cast<ListViewItem.ListViewSubItem>().Any(si => si.Text.IndexOf(entityName, StringComparison.InvariantCultureIgnoreCase) >= 0))
                    .ToArray();
                lvEntities.Items.AddRange(filteredItems);
                lvEntities.EndUpdate();
            }
        }

        private void TsbPublishAllClick(object sender, EventArgs e)
        {
            tsbPublishEntity.Enabled = false;
            tsbPublishAll.Enabled = false;
            tsbSaveViews.Enabled = false;
            tssbLoadTables.Enabled = false;
            tsbSaveAndPublish.Enabled = false;

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Publishing all customizations...",
                AsyncArgument = null,
                Work = (bw, evt) =>
                {
                    var pubRequest = new PublishAllXmlRequest();
                    Service.Execute(pubRequest);
                },
                PostWorkCallBack = evt =>
                {
                    if (evt.Error != null)
                    {
                        string errorMessage = CrmExceptionHelper.GetErrorMessage(evt.Error, false);
                        MessageBox.Show(this, errorMessage, "Error", MessageBoxButtons.OK,
                                                          MessageBoxIcon.Error);
                    }

                    tsbPublishEntity.Enabled = true;
                    tsbPublishAll.Enabled = true;
                    tsbSaveViews.Enabled = true;
                    tssbLoadTables.Enabled = true;
                    tsbSaveAndPublish.Enabled = true;
                }
            });
        }

        private void tsbSaveAndPublish_Click(object sender, EventArgs e)
        {
            SaveViews(true);
        }

        private void tssbLoadTablesFromSolution_ButtonClick(object sender, EventArgs e)
        {
            LoadEntities(true);
        }

        private void tssbLoadTablesFromSolution_DropDownItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            if (e.ClickedItem == tsmiLoadAllTables)
            {
                LoadEntities();
            }
        }
    }
}