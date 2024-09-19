using Cairo;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace PlumbandCube
{
    public class PlumbandCube : Item
    {
        private WorldInteraction[] interactions;

        private List<LoadedTexture> symbols;

        private int reinforcementCount = 0;
        private int lastItemStregnth = 0;


        public override void OnLoaded(ICoreAPI api)
        {
            if (api.Side != EnumAppSide.Client)
            {
                return;
            }

            _ = api;
            interactions = ObjectCacheUtil.GetOrCreate(api, "plumbAndSquareInteractions", delegate
            {
                List<ItemStack> list = new List<ItemStack>();
                foreach (CollectibleObject collectible in api.World.Collectibles)
                {
                    JsonObject attributes = collectible.Attributes;
                    if (attributes != null && attributes["reinforcementStrength"].AsInt() > 0)
                    {
                        list.Add(new ItemStack(collectible));
                    }
                }

                return new WorldInteraction[2]
                {
                new WorldInteraction
                {
                    ActionLangCode = "heldhelp-reinforceblock",
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = list.ToArray()
                },
                new WorldInteraction
                {
                    ActionLangCode = "heldhelp-removereinforcement",
                    MouseButton = EnumMouseButton.Left,
                    Itemstacks = list.ToArray()
                }
                };
            });
            symbols = new List<LoadedTexture>();
            symbols.Add(GenTexture(1, 1));
        }

        public override void OnUnloaded(ICoreAPI api)
        {
            base.OnUnloaded(api);
            if (!(api is ICoreClientAPI) || symbols == null)
            {
                return;
            }

            foreach (LoadedTexture symbol in symbols)
            {
                symbol.Dispose();
            }
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
            if (handling == EnumHandHandling.PreventDefault)
            {
                return;
            }

            if (byEntity.World.Side == EnumAppSide.Client)
            {
                handling = EnumHandHandling.PreventDefaultAction;
            }
            else
            {
                if (blockSel == null)
                {
                    return;
                }

                ModSystemBlockReinforcement modSystem = byEntity.Api.ModLoader.GetModSystem<ModSystemBlockReinforcement>();
                IPlayer player = (byEntity as EntityPlayer).Player;
                if (player == null)
                {
                    return;
                }

                ItemSlot itemSlot = modSystem.FindResourceForReinforcing(player);
                if (itemSlot == null)
                {
                    return;
                }

                int strength = itemSlot.Itemstack.ItemAttributes["reinforcementStrength"].AsInt();
                int @int = slot.Itemstack.Attributes.GetInt("toolMode");
                int num = 0;
                PlayerGroupMembership[] groups = player.GetGroups();
                if (@int > 0 && @int - 1 < groups.Length)
                {
                    num = groups[@int - 1].GroupUid;
                }

                if (!api.World.BlockAccessor.GetBlock(blockSel.Position).HasBehavior<BlockBehaviorReinforcable>())
                {
                    (player as IServerPlayer).SendIngameError("notreinforcable", "This block can not be reinforced!");
                    return;
                }


                BlockPos min = new BlockPos(blockSel.Position.dimension);
                BlockPos max = new BlockPos(blockSel.Position.dimension);
                switch (blockSel.Face.Axis)
                {
                    case EnumAxis.X:
                        min = blockSel.Position.AddCopy(0, -2, -2);
                        max = blockSel.Position.AddCopy(0, 2, 2);
                        break;
                    case EnumAxis.Y:
                        min = blockSel.Position.AddCopy(-2, 0, -2);
                        max = blockSel.Position.AddCopy(2, 0, 2);
                        break;
                    case EnumAxis.Z:
                        min = blockSel.Position.AddCopy(-2, -2, 0);
                        max = blockSel.Position.AddCopy(2, 2, 0);
                        break;
                }

                BlockPos tempPos = new BlockPos(blockSel.Position.dimension);
                for (int x = min.X; x <= max.X; x++)
                {
                    for (int y = min.Y; y <= max.Y; y++)
                    {
                        for (int z = min.Z; z <= max.Z; z++)
                        {
                            tempPos.Set(x, y, z);

                            if (reinforcementCount <= 0)
                            {
                                reinforcementCount = 25;
                                itemSlot.TakeOut(1);
                                itemSlot.MarkDirty();

                            }

                            if (!((num > 0) ? modSystem.StrengthenBlock(tempPos, player, strength, num) : modSystem.StrengthenBlock(tempPos, player, strength)))
                            {
                                (player as IServerPlayer).SendIngameError("alreadyreinforced", "Cannot reinforce block, it's already reinforced!");
                            }
                            else
                            {
                                reinforcementCount--;
                            }
                        }
                    }
                }


                BlockPos position = blockSel.Position;
                byEntity.World.PlaySoundAt(new AssetLocation("sounds/tool/reinforce"), position.X, position.Y, position.Z);
                handling = EnumHandHandling.PreventDefaultAction;
                if (byEntity.World.Side == EnumAppSide.Client)
                {
                    ((byEntity as EntityPlayer)?.Player as IClientPlayer).TriggerFpAnimation(EnumHandInteract.HeldItemInteract);
                }
            }
        }

        public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
        {
            if (byEntity.World.Side == EnumAppSide.Client)
            {
                handling = EnumHandHandling.PreventDefaultAction;
            }
            else
            {
                if (blockSel == null)
                {
                    return;
                }

                ModSystemBlockReinforcement modSystem = byEntity.Api.ModLoader.GetModSystem<ModSystemBlockReinforcement>();
                if (!((byEntity as EntityPlayer).Player is IServerPlayer serverPlayer))
                {
                    return;
                }

                BlockReinforcement reinforcment = modSystem.GetReinforcment(blockSel.Position);
                string errorCode = "";


                BlockPos min = new BlockPos(blockSel.Position.dimension);
                BlockPos max = new BlockPos(blockSel.Position.dimension);
                switch (blockSel.Face.Axis)
                {
                    case EnumAxis.X:
                        min = blockSel.Position.AddCopy(0, -2, -2);
                        max = blockSel.Position.AddCopy(0, 2, 2);
                        break;
                    case EnumAxis.Y:
                        min = blockSel.Position.AddCopy(-2, 0, -2);
                        max = blockSel.Position.AddCopy(2, 0, 2);
                        break;
                    case EnumAxis.Z:
                        min = blockSel.Position.AddCopy(-2, -2, 0);
                        max = blockSel.Position.AddCopy(2, 2, 0);
                        break;
                }

                BlockPos tempPos = new BlockPos(blockSel.Position.dimension);
                for (int x = min.X; x <= max.X; x++)
                {
                    for (int y = min.Y; y <= max.Y; y++)
                    {
                        for (int z = min.Z; z <= max.Z; z++)
                        {
                            tempPos.Set(x, y, z);
                            if (!modSystem.TryRemoveReinforcement(tempPos, serverPlayer, ref errorCode))
                            {
                                if (errorCode == "notownblock")
                                {
                                    serverPlayer.SendIngameError("cantremove", "Cannot remove reinforcement. This block does not belong to you");
                                }
                                else
                                {
                                    serverPlayer.SendIngameError("cantremove", "Cannot remove reinforcement. It's not reinforced");
                                }
                            }

                            if (reinforcment.Locked)
                            {
                                ItemStack itemstack = new ItemStack(byEntity.World.GetItem(new AssetLocation(reinforcment.LockedByItemCode)));
                                if (!serverPlayer.InventoryManager.TryGiveItemstack(itemstack, slotNotifyEffect: true))
                                {
                                    byEntity.World.SpawnItemEntity(itemstack, byEntity.ServerPos.XYZ);
                                }
                            }
                        }
                    }
                }

                BlockPos position = blockSel.Position;
                byEntity.World.PlaySoundAt(new AssetLocation("sounds/tool/reinforce"), position.X, position.Y, position.Z);
                handling = EnumHandHandling.PreventDefaultAction;
            }
        }

        public override void SetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection, int toolMode)
        {
            slot.Itemstack.Attributes.SetInt("toolMode", toolMode);
        }

        public override int GetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection)
        {
            return Math.Min(1 + byPlayer.GetGroups().Length - 1, slot.Itemstack.Attributes.GetInt("toolMode"));
        }

        public override SkillItem[] GetToolModes(ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel)
        {
            PlayerGroupMembership[] groups = forPlayer.GetGroups();
            SkillItem[] array = new SkillItem[1 + groups.Length];
            ICoreClientAPI capi = api as ICoreClientAPI;
            int num = 1;
            LoadedTexture texture = FetchOrCreateTexture(num);
            array[0] = new SkillItem
            {
                Code = new AssetLocation("self"),
                Name = Lang.Get("Reinforce for yourself")
            }.WithIcon(capi, texture);
            for (int i = 0; i < groups.Length; i++)
            {
                texture = FetchOrCreateTexture(++num);
                array[i + 1] = new SkillItem
                {
                    Code = new AssetLocation("group"),
                    Name = Lang.Get("Reinforce for group " + groups[i].GroupName)
                }.WithIcon(capi, texture);
            }

            return array;
        }

        private LoadedTexture FetchOrCreateTexture(int seed)
        {
            if (symbols.Count >= seed)
            {
                return symbols[seed - 1];
            }

            LoadedTexture loadedTexture = GenTexture(seed, seed);
            symbols.Add(loadedTexture);
            return loadedTexture;
        }

        private LoadedTexture GenTexture(int seed, int addLines)
        {
            ICoreClientAPI capi = api as ICoreClientAPI;
            return capi.Gui.Icons.GenTexture(48, 48, delegate (Context ctx, ImageSurface surface)
            {
                capi.Gui.Icons.DrawRandomSymbol(ctx, 0.0, 0.0, 48.0, GuiStyle.MacroIconColor, 2.0, seed, addLines);
            });
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            return interactions.Append(base.GetHeldInteractionHelp(inSlot));
        }
    }
}
