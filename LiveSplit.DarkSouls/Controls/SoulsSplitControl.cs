﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using System.Xml;
using LiveSplit.DarkSouls.Data;

namespace LiveSplit.DarkSouls.Controls
{
	public partial class SoulsSplitControl : UserControl
	{
		private const int ControlSpacing = 4;
		private const int ItemSplitCorrection = 2;
		private const int ItemCountIndex = 4;
		private const int FlagIdIndex = 0;

		private static SplitLists lists;
		private static Dictionary<string, string[]> itemMap;
		private static Dictionary<string, int> modReinforcementMap;

		// This value can be static since only one split can be dragged at one time.
		private static int dragAnchor;

		static SoulsSplitControl()
		{
			modReinforcementMap = new Dictionary<string, int>
			{
				{ "Basic", 15 },
				{ "Chaos", 5 },
				{ "Crystal", 5 },
				{ "Divine", 10 },
				{ "Enchanted", 5 },
				{ "Fire", 10 },
				{ "Lightning", 5 },
				{ "Magic", 10 },
				{ "Occult", 5 },
				{ "Raw", 5 }
			};

			lists = SplitLists.Load();

			var items = lists.Items;

			itemMap = new Dictionary<string, string[]>
			{
				{ "Ammunition", items.Ammunition },
				{ "Axes", items.Axes },
				{ "Bonfire", items.Bonfire },
				{ "Bows", items.Bows },
				{ "Catalysts", items.Catalysts },
				{ "Chest Pieces", items.ChestPieces },
				{ "Consumables", items.Consumables },
				{ "Covenant", items.Covenant },
				{ "Crossbows", items.Crossbows },
				{ "Daggers", items.Daggers },
				{ "Embers", items.Embers },
				{ "Fist", items.Fist },
				{ "Flames", items.Flames },
				{ "Gauntlets", items.Gauntlets },
				{ "Greatswords", items.Greatswords },
				{ "Halberds", items.Halberds },
				{ "Hammers", items.Hammers },
				{ "Helmets", items.Helmets },
				{ "Keys", items.Keys },
				{ "Leggings", items.Leggings },
				{ "Miracles", items.Miracles },
				{ "Multiplayer", items.Multiplayer },
				{ "Ore", items.Ore },
				{ "Projectiles", items.Projectiles },
				{ "Pyromancies", items.Pyromancies },
				{ "Rings", items.Rings },
				{ "Shields", items.Shields },
				{ "Sorceries", items.Sorceries },
				{ "Souls", items.Souls },
				{ "Spears", items.Spears },
				{ "Swords", items.Swords },
				{ "Talismans", items.Talismans },
				{ "Tools", items.Tools },
				{ "Trinkets", items.Trinkets },
				{ "Whips", items.Whips },
			};
		}

		private Dictionary<SplitTypes, Func<Control[]>> functionMap;
		private SoulsSplitCollectionControl parent;

		private bool dragActive;

		// Equipment lists need to track upgrade data per item in order to properly update secondary item lines. This
		// array is updated whenever equipment type changes.
		private int[] upgrades;

		private bool modsApplicable;

		// Tracking this value is required to properly shrink item splits that swap to a different type.
		private SplitTypes previousSplitType;

		public SoulsSplitControl(SoulsSplitCollectionControl parent, int index)
		{
			this.parent = parent;

			Index = index;
			previousSplitType = SplitTypes.Unassigned;

			InitializeComponent();

			functionMap = new Dictionary<SplitTypes, Func<Control[]>>()
			{
				{ SplitTypes.Bonfire, GetBonfireControls },
				{ SplitTypes.Boss, GetBossControls },
				{ SplitTypes.Covenant, GetCovenantControls },
				{ SplitTypes.Event, GetEventControls },
				{ SplitTypes.Flag, GetFlagControls },
				{ SplitTypes.Item, GetItemControls },
				{ SplitTypes.Quitout, GetQuitoutControls },
				{ SplitTypes.Zone, GetZoneControls }
			};	
		}

		// Indices can be updated if earlier splits are removed.
		public int Index { get; set; }

		// This field helps inform users when any split in the UI is unfinished (through text at the top of the
		// configuration window).
		public bool IsFinished { get; private set; }

		public Split ExtractSplit()
		{
			var index = splitTypeComboBox.SelectedIndex;
			var type = index != -1 ? (SplitTypes)index : SplitTypes.Unassigned;
			var controls = splitDetailsPanel.Controls;

			int[] data = null;

			if (controls.Count > 0)
			{
				data = new int[controls.Count];

				for (int i = 0; i < controls.Count; i++)
				{
					Control control = controls[i];

					// A few split types use a textbox. All other controls are dropdowns.
					if (control is TextBox textbox)
					{
						string text = textbox.Text;

						data[i] = text.Length > 0 ? int.Parse(text) : -1;

						continue;
					}

					var dropdown = (ComboBox)control;
					int selectedIndex = dropdown.SelectedIndex;

					// -1 as a selected index isn't always invalid (it's valid if the combo box is disabled). Storing a
					// positive value in these cases allows splits to determine whether they're actually valid on load.
					data[i] = dropdown.Enabled ? selectedIndex : (selectedIndex == -1 ? int.MaxValue : selectedIndex);
				}
			}

			return new Split(type, data);
		}

		public void Refresh(Split split)
		{
			SplitTypes type = split.Type;

			if (type == SplitTypes.Unassigned)
			{
				return;
			}
			
			splitTypeComboBox.SelectedIndex = (int)type;

			var controls = splitDetailsPanel.Controls;

			// If a valid split type was selected, the data array is guaranteed to exist (although it might still have
			// -1 values).
			int[] data = split.Data;

			for (int i = 0; i < controls.Count; i++)
			{
				Control control = controls[i];

				if (control is TextBox textbox)
				{
					int value = data[i];

					textbox.Text = value != -1 ? value.ToString() : "";

					continue;
				}

				var dropdown = (ComboBox)control;
				int index = data[i];
				
				dropdown.SelectedIndex = dropdown.Enabled ? index : (index == int.MaxValue ? -1 : index);
			}

			// For non-manual splits, the finished state will already have been refreshed (as dropdowns are set).
			// Quitouts are also finished by default (since they use no additional controls).
			if (type == SplitTypes.Manual || type == SplitTypes.Quitout)
			{
				RefreshFinished(true);
			}
		}

		// This function is called from dropdowns in the details section of the split. Some changes (such as the
		// selected index resetting to -1) are guaranteed to invalidate the split.
		public void RefreshFinished(bool? value = null)
		{
			bool previouslyFinished = IsFinished;

			IsFinished = value ?? ComputeFinished();

			if (IsFinished && !previouslyFinished)
			{
				parent.UnfinishedCount--;
			}
			else if (previouslyFinished && !IsFinished)
			{
				parent.UnfinishedCount++;
			}
		}

		private bool ComputeFinished()
		{
			var index = splitTypeComboBox.SelectedIndex;
			var type = index != -1 ? (SplitTypes)index : SplitTypes.Unassigned;

			switch (type)
			{
				case SplitTypes.Manual: return true;
				case SplitTypes.Quitout: return true;
				case SplitTypes.Unassigned: return false;
			}

			var controls = splitDetailsPanel.Controls;

			for (int i = 0; i < controls.Count; i++)
			{
				var control = controls[i];

				// It's impossible for the item count textbox to be invalid (since it only accepts numeric input
				// and is reset to one when focus is lost).
				if (!control.Enabled || (type == SplitTypes.Item && i == ItemCountIndex))
				{
					continue;
				}

				// In contrast to item count, the flag ID textbox can be empty.
				if (type == SplitTypes.Flag && i == FlagIdIndex)
				{
					// Note that the flag itself isn't validated (as far as being an ID that actually exists in the
					// game). That validation could be done using another ID list, but that approach doesn't seem worth
					// the effort (and added DLL file size).
					return ((TextBox)control).Text.Length > 0;
				}

				if (((ComboBox)control).SelectedIndex == -1)
				{
					return false;
				}
			}

			return true;
		}

		private void deleteButton_Click(object sender, EventArgs e)
		{
			parent.RemoveSplit(Index);
		}

		private void dragImage_MouseDown(object sender, MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				int globalY = e.Location.Y + Location.Y;

				dragActive = true;
				dragAnchor = globalY - Location.Y;

				// This color is very slightly darker than the default and helps the dragged split stand out.
				BackColor = SystemColors.ControlLight;

				parent.BeginDrag(this);
			}
		}

		private void dragImage_MouseMove(object sender, MouseEventArgs e)
		{
			if (dragActive)
			{
				int globalY = e.Location.Y + Location.Y;

				// Splits can only be dragged vertically.
				parent.UpdateDrag(globalY - dragAnchor);
			}
		}

		private void dragImage_MouseUp(object sender, MouseEventArgs e)
		{
			parent.Drop();
			BackColor = SystemColors.ButtonFace;
			dragActive = false;
		}

		private void splitTypeComboBox_SelectedIndexChanged(object sender, EventArgs e)
		{
			OnSplitTypeChange((SplitTypes)splitTypeComboBox.SelectedIndex);
		}

		private void OnSplitTypeChange(SplitTypes splitType)
		{
			bool previouslyFinished = IsFinished;

			Control[] controls = splitType != SplitTypes.Manual ? functionMap[splitType]() : null;
			ControlCollection panelControls = splitDetailsPanel.Controls;
			panelControls.Clear();

			if (controls != null)
			{
				for (int i = 0; i < controls.Length; i++)
				{
					var control = controls[i];

					if (i > 0)
					{
						control.Location = new Point(controls[i - 1].Bounds.Right + ControlSpacing, 0);
					}
					
					panelControls.Add(control);
				}
			}

			// Item splits require two lines.
			if (splitType == SplitTypes.Item)
			{
				Height = Height * 2 - ItemSplitCorrection;
				splitDetailsPanel.Height = splitDetailsPanel.Height * 2 + ControlSpacing;

				Control[] secondaryControls = GetItemSecondaryControls();

				int y = controls[0].Bounds.Bottom + ControlSpacing;

				for (int i = 0; i < secondaryControls.Length; i++)
				{
					Control control = secondaryControls[i];
					Point point = new Point(0, y);

					if (i > 0)
					{
						point.X = secondaryControls[i - 1].Bounds.Right + ControlSpacing;
					}

					control.Location = point;
					panelControls.Add(control);

					LinkItemLines(controls, secondaryControls);
				}

				// Changing an existing split to an item split can cause later splits to be shifted down.
				if (Index < parent.SplitCount - 1)
				{
					parent.ShiftSplits(Index + 1);
				}
			}
			else if (previousSplitType == SplitTypes.Item)
			{
				Height = (Height + ItemSplitCorrection) / 2;
				splitDetailsPanel.Height = (splitDetailsPanel.Height - ControlSpacing) / 2;
				parent.ShiftSplits(Index + 1);
			}

			if (previouslyFinished)
			{
				// Changing the split type always adds new, empty controls, which means the new split is unfinished by
				// default (unless it's manual).
				if (splitType != SplitTypes.Manual)
				{
					parent.UnfinishedCount++;
					IsFinished = false;
				}
			}
			else if (splitType == SplitTypes.Manual || splitType == SplitTypes.Quitout)
			{
				parent.UnfinishedCount--;
				IsFinished = true;
			}

			previousSplitType = splitType;
		}

		private Control[] GetBonfireControls()
		{
			const int BonfireListWidth = 169;
			const int BonfireCriteriaWidth = 119;

			var bonfireCriteria = GetDropdown(new []
			{
				"On light",
				"On rest",
				"On leave",
				"On kindle (first)",
				"On kindle (second)",
				"On kindle (third)",
				"On warp"
			}, "Criteria", BonfireCriteriaWidth, false);

			var bonfireList = GetDropdown(lists.Bonfires, "Bonfire", BonfireListWidth);
			bonfireList.SelectedIndexChanged += (sender, args) =>
			{
				bonfireCriteria.Enabled = true;
			};

			return new Control[]
			{
				bonfireList,
				bonfireCriteria
			};
		}

		private Control[] GetBossControls()
		{
			const int BossListWidth = 131;
			const int BossTimingWidth = 122;

			var bossTiming = GetDropdown(new []
			{
				"On victory message",
				"On quitout",
				"On warp"
			}, "Timing", BossTimingWidth, false);

			var bossList = GetDropdown(lists.Bosses, "Bosses", BossListWidth);
			bossList.SelectedIndexChanged += (sender, args) =>
			{
				bossTiming.Enabled = true;
			};

			return new Control[]
			{
				bossList,
				bossTiming
			};
		}

		private Control[] GetCovenantControls()
		{
			const int CovenantListWidth = 139;
			const int CovenantCriteriaWidth = 86;
			const int CovenantTimingWidth = 78;

			var covenantTiming = GetDropdown(new []
			{
				"On trigger",
				"On quitout",
				"On warp"
			}, "Timing", CovenantTimingWidth, false);

			var covenantCriteria = GetDropdown(new []
			{
				"On discover",
				"On join"
			}, "Criteria", CovenantCriteriaWidth, false);

			covenantCriteria.SelectedIndexChanged += (sender, args) =>
			{
				covenantTiming.Enabled = true;
			};

			var covenantList = GetDropdown(lists.Covenants, "Covenants", CovenantListWidth);
			covenantList.SelectedIndexChanged += (sender, args) =>
			{
				covenantCriteria.Enabled = true;
			};

			return new Control[]
			{
				covenantList,
				covenantCriteria,
				covenantTiming
			};
		}

		private Control[] GetEventControls()
		{
			const int EventListWidth = 88;
			const int EventTimingWidth = 78;

			// Criteria items are added below as needed.
			var eventTiming = GetDropdown(null, "Timing", EventTimingWidth, false);
			var eventList = GetDropdown(new []
			{
				"- Bells -",
				"First Bell",
				"Second Bell",
				"",
				"- Endings -",
				"Dark Lord",
				"Link the Fire"
			}, "Event", EventListWidth);
			
			eventList.SelectedIndexChanged += (sender, args) =>
			{
				bool isBell = eventList.SelectedIndex <= 2;

				if (isBell)
				{
					if (eventTiming.SelectedIndex == -1)
					{
						eventTiming.RefreshPrompt("Timing", true);
						eventTiming.Items.AddRange(new []
						{
							"On ring",
							"On quitout",
							"On warp"
						});
					}
				}
				else
				{
					eventTiming.RefreshPrompt("N/A");
				}
			};

			return new Control[]
			{
				eventList,
				eventTiming
			};
		}

		private Control[] GetFlagControls()
		{
			const int FlagIdWidth = 68;
			const int FlagTimingWidth = 78;

			Color unfinishedColor = Color.PaleVioletRed;

			var flagId = GetNumericTextbox(FlagIdWidth, 8, true);

			flagId.BackColor = unfinishedColor;
			flagId.TextChanged += (sender, args) =>
			{
				flagId.BackColor = flagId.Text.Length == 0 ? unfinishedColor : Color.White;
				RefreshFinished();
			};

			var flagTiming = GetDropdown(new []
			{
				"On trigger",
				"On quitout",
				"On warp"
			}, "Timing", FlagTimingWidth);

			return new Control[]
			{
				flagId,
				flagTiming
			};
		}

		private Control[] GetItemControls()
		{
			const int ItemTypeWidth = 93;
			const int ItemListWidth = 224;

			var itemList = GetDropdown(null, "Items", ItemListWidth, false);

			// Additional callbacks are added when item lines are linked.
			var itemTypes = GetDropdown(new []
			{
				"- Armor -",
				"Chest Pieces",
				"Gauntlets",
				"Helmets",
				"Leggings",
				"",
				"- Magic -",
				"Catalysts",
				"Flames",
				"Miracles",
				"Pyromancies",
				"Sorceries",
				"Talismans",
				"",
				"- Miscellaneous -",
				"Bonfire",
				"Consumables",
				"Covenant",
				"Keys",
				"Multiplayer",
				"Souls",
				"Trinkets",
				"",

				// Ideally, this category would read "Other Equipment", but it's shortened to better fit into the 
				// LiveSplit window.
				"- Equipment -",
				"Ammunition",
				"Projectiles",
				"Rings",
				"Shields",
				"Tools",
				"",
				"- Smithing -",
				"Embers",
				"Ore",
				"",
				"- Weapons -",
				"Axes",
				"Bows",
				"Crossbows",
				"Daggers",
				"Fist",
				"Greatswords",
				"Halberds",
				"Hammers",
				"Spears",
				"Swords",
				"Whips"
			}, "Item Types", ItemTypeWidth);

			return new Control[]
			{
				itemTypes,
				itemList
			};
		}

		private Control[] GetItemSecondaryControls()
		{
			const int ItemModificationWidth = 81;
			const int ItemReinforcementWidth = 100;
			const int ItemCountWidth = 32;
			const int ItemTimingWidth = 96;

			// Mods and reinforcements are updated based on item type.
			var itemModifications = GetDropdown(null, "Infusions", ItemModificationWidth, false);
			var itemReinforcements = GetDropdown(null, "Reinforcement", ItemReinforcementWidth, false);
			var itemTiming = GetDropdown(new[]
			{
				"On acquisition",
				"On quitout",
				"On warp"
			}, "Timing", ItemTimingWidth);

			// The item count textbox was originally on line one, but it was moved down to accomodate extra width
			// needed for the item list.
			var itemCount = GetNumericTextbox(ItemCountWidth, 3, false, 1, 1);

			return new Control[]
			{
				itemModifications,
				itemReinforcements,
				itemCount,
				itemTiming
			};
		}

		private void LinkItemLines(Control[] line1, Control[] line2)
		{
			// Infusions are called "modifications" in most of the codebase. Swapping to "infusions" happened pretty
			// late in the project, so it wasn't worth renaming everything internally.
			const string ModString = "Infusions";
			const string ReinforceString = "Reinforcement";

			// There were originally two strings here ("Uninfusable" and "Unreinforceable"). They were later replaced
			// with "Locked" in order to reclaim space (since LiveSplit's window width is fixed).
			const string LockedString = "Locked";
			const string NaString = "N/A";

			SoulsDropdown itemTypes = (SoulsDropdown)line1[0];
			SoulsDropdown itemList = (SoulsDropdown)line1[1];
			SoulsDropdown mods = (SoulsDropdown)line2[0];
			SoulsDropdown reinforcements = (SoulsDropdown)line2[1];

			TextBox itemCount = (TextBox)line2[2];

			itemTypes.SelectedIndexChanged += (sender, args) =>
			{
				const int FlameIndex = 8;
				const int ShieldIndex = 27;
				const int WeaponIndex = 34;

				itemList.Enabled = true;
				itemCount.Enabled = true;
				itemCount.Text = "1";

				var rawList = itemMap[itemTypes.Text];
				var items = itemList.Items;

				// Refreshing the prompt (rather than just clearing items) also resets the background color to red.
				itemList.RefreshPrompt("Items", true);

				int typeIndex = itemTypes.SelectedIndex;

				bool isArmorType = typeIndex >= 1 && typeIndex <= 4;
				bool isShield = typeIndex == ShieldIndex;
				bool isWeaponType = typeIndex >= WeaponIndex;
				bool isFlame = typeIndex == FlameIndex;

				modsApplicable = isWeaponType || isShield;

				if (isArmorType || isShield || isWeaponType || isFlame)
				{
					upgrades = new int[rawList.Length];

					for (int i = 0; i < rawList.Length; i++)
					{
						string value = rawList[i];

						if (value.Length == 0 || value[0] == '-')
						{
							items.Add(value);

							continue;
						}

						string[] tokens = value.Split('|');
						string name = tokens[0];

						// Armor and weapons store upgrade data differently. Since armor can't be modified, the maximum
						// reinforcement value is stored. For weapons, the modification category is stored, which in
						// turn informs reinforcement.
						int upgradeValue;

						if (!modsApplicable)
						{
							string reinforcementString = tokens[1];

							upgradeValue = reinforcementString[0] == '+'
								? int.Parse(reinforcementString.Substring(1))
								: 0;
						}
						// Weapons and shields follow the same logic for toggling mods and reinforcement.
						else
						{
							upgradeValue = (int)Enum.Parse(typeof(ModificationTypes), tokens[1]);
						}

						items.Add(name);
						upgrades[i] = upgradeValue;
					}

					mods.RefreshPrompt(modsApplicable ? ModString : NaString);
					reinforcements.RefreshPrompt(ReinforceString);
				}
				else
				{
					items.AddRange(rawList);
					mods.RefreshPrompt(NaString);
					reinforcements.RefreshPrompt(NaString);
					upgrades = null;
				}
			};

			itemList.SelectedIndexChanged += (sender, args) =>
			{
				const int ConsumableIndex = 16;
				const int EstusIndex = 6;

				int index = itemList.SelectedIndex;

				// The estus flask can be upgraded, so some special logic is needed to account for that.
				bool isConsumable = itemTypes.SelectedIndex == ConsumableIndex;
				bool isEstus = isConsumable && index == EstusIndex;

				// The upgrade array being null means that the current item type is not equipment.
				if (upgrades == null && !isEstus)
				{
					bool previouslyEstus = isConsumable && itemList.PreviousIndex == EstusIndex;

					if (previouslyEstus)
					{
						reinforcements.RefreshPrompt("N/A");
					}

					return;
				}

				// The maximum estus upgrade is +7.
				int data = isEstus ? 7 : upgrades[index];

				// Armor and flames can be reinforced, but never modified.
				if (!modsApplicable)
				{
					if (data > 0)
					{
						reinforcements.RefreshPrompt(ReinforceString, true);
						reinforcements.Items.AddRange(GetReinforcementList(data));
					}
					else
					{
						reinforcements.RefreshPrompt(LockedString);
					}
				}
				else
				{
					ModificationTypes modType = (ModificationTypes)data;

					switch (modType)
					{
						case ModificationTypes.None:
							mods.RefreshPrompt(LockedString);
							reinforcements.RefreshPrompt(LockedString);

							break;

						// "Special" in this context means that the weapon is unique. It can be reinforced up to +5,
						// but can't be modified.
						case ModificationTypes.Special:
							mods.RefreshPrompt(LockedString);
							reinforcements.RefreshPrompt(ReinforceString, true);
							reinforcements.Items.AddRange(GetReinforcementList(5));

							break;

						// For standard weapons, the maximum reinforcement is based on mod type.
						default:
							// Crossbows and shields use a restricted set of mods. Max reinforcement values are the
							// same for each type.
							var availableMods = modType == ModificationTypes.Standard
								? modReinforcementMap.Keys.ToArray()
								: new []
								{
									"Basic",
									"Crystal",
									"Lightning",
									"Magic",
									"Divine",
									"Fire"
								};

							mods.RefreshPrompt(ModString, true);
							mods.Items.AddRange(availableMods);
							reinforcements.RefreshPrompt(ReinforceString);

							break;
					}
				}
			};

			mods.SelectedIndexChanged += (sender, args) =>
			{
				// This function can only be called for standard (i.e. modifiable) weapons.
				int max = modReinforcementMap[mods.Text];

				reinforcements.RefreshPrompt(ReinforceString, true);
				reinforcements.Items.AddRange(GetReinforcementList(max));
			};
		}

		private string[] GetReinforcementList(int max)
		{
			string[] results = new string[max + 1];
			results[0] = "+0 [None]";

			for (int i = 1; i <= max; i++)
			{
				results[i] = "+" + i;
			}

			return results;
		}

		private Control[] GetQuitoutControls()
		{
			const int QuitoutCountWidth = 32;

			var quitoutCount = GetNumericTextbox(QuitoutCountWidth, 2, true, 1, 1);

			return new Control[]
			{
				quitoutCount
			};
		}

		private Control[] GetZoneControls()
		{
			const int ZoneListWidth = 152;

			var zoneList = GetDropdown(lists.Zones, "Zones", ZoneListWidth);

			return new Control[] { zoneList };
		}

		private SoulsDropdown GetDropdown(string[] items, string prompt, int width, bool enabled = true)
		{
			SoulsDropdown box = new SoulsDropdown(this)
			{
				Enabled = enabled,
				Width = width,
				Prompt = prompt
			};

			if (items != null)
			{
				box.Items.AddRange(items);
			}

			// See https://stackoverflow.com/a/1883072/7281613.
			box.MouseWheel += (sender, args) =>
			{
				if (!box.DroppedDown)
				{
					((HandledMouseEventArgs)args).Handled = true;
				}
			};

			return box;
		}

		private TextBox GetNumericTextbox(int width, int maxLength, bool enabled, int? value = null,
			int? forcedValue = null)
		{
			var textbox = new TextBox
			{
				Enabled = enabled,
				Width = width,
				AutoSize = false,

				// This height results in the text box exactly lining up with adjacent dropdowns.
				Height = 21,
				MaxLength = maxLength,
				TextAlign = HorizontalAlignment.Center
			};

			if (value.HasValue)
			{
				textbox.Text = value.Value.ToString();
			}

			// See https://stackoverflow.com/q/463299/7281613.
			textbox.KeyPress += (sender, args) =>
			{
				char c = args.KeyChar;

				// See http://www.asciitable.com/ as well. The idea here is to accept all control keys (such as Ctrl +
				// V) while still excluding letters and punctuation.
				if (c >= '!' && c <= '~' && !char.IsDigit(args.KeyChar))
				{
					args.Handled = true;
				}
			};

			textbox.KeyDown += (sender, args) =>
			{
				// Pressing escape removes focus from the textbox.
				if (args.KeyCode == Keys.Escape)
				{
					ParentForm.Controls[0].Focus();
				}
			};

			textbox.TextChanged += (sender, args) =>
			{
				string text = textbox.Text;

				if (text.Length == 0 || int.TryParse(text, out int result))
				{
					return;
				}

				// It's possible to paste a non-integer into the textbox (like something with letters, for example). If
				// the above check fails, then, all non-digits are stripped from the pasted value.
				StringBuilder builder = new StringBuilder();

				foreach (char c in text)
				{
					if (char.IsDigit(c))
					{
						builder.Append(c);
					}
				}

				// This actually causes the TextChanged event to be triggered again, but the function returns
				// immediately due to the checks above.
				textbox.Text = builder.ToString();
			};

			textbox.Enter += (sender, args) =>
			{
				// See https://stackoverflow.com/a/6857301/7281613. This causes all text to be selected after the click
				// is processed.
				BeginInvoke((Action)textbox.SelectAll);
			};

			// "Forced value" means that the textbox can never be saved empty (since losing focus while empty fills in
			// the given value).
			if (forcedValue.HasValue)
			{
				textbox.LostFocus += (sender, args) =>
				{
					// This ensures that the textbox always ends up with a valid value.
					if (textbox.Text.Length == 0)
					{
						textbox.Text = forcedValue.Value.ToString();
					}
				};
			}

			return textbox;
		}
	}
}
