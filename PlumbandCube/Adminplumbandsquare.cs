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
    internal class Adminplumbandsquare : Item
    {
            WorldInteraction[] interactions;
            List<LoadedTexture> symbols;

            const int ADMIN_REINFORCE_STRENGTH = 99999;

            public override void OnLoaded(ICoreAPI api)
            {
                if (api.Side != EnumAppSide.Client) return;
                ICoreClientAPI capi = api as ICoreClientAPI;

                interactions = ObjectCacheUtil.GetOrCreate(api, "plumbAndSquareInteractions", () =>
                {
                    List<ItemStack> stacks = new List<ItemStack>();

                    foreach (CollectibleObject obj in api.World.Collectibles)
                    {
                        if (obj.Attributes?["reinforcementStrength"].AsInt(0) > 0)
                        {
                            stacks.Add(new ItemStack(obj));
                        }
                    }

                    return new WorldInteraction[]
                    {
                    new WorldInteraction()
                    {
                        ActionLangCode = "heldhelp-reinforceblock",
                        MouseButton = EnumMouseButton.Right,
                        Itemstacks = stacks.ToArray()
                    },
                    new WorldInteraction()
                    {
                        ActionLangCode = "heldhelp-removereinforcement",
                        MouseButton = EnumMouseButton.Left,
                        Itemstacks = stacks.ToArray()
                    }
                    };
                });

                symbols = new List<LoadedTexture>();
                symbols.Add(GenTexture(1, 1));
            }

            public override void OnUnloaded(ICoreAPI api)
            {
                base.OnUnloaded(api);
                if (api is ICoreClientAPI && symbols != null)
                {
                    foreach (var texture in symbols) texture.Dispose();
                }
            }


            public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
            {
                base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
                if (handling == EnumHandHandling.PreventDefault) return;

                if (byEntity.World.Side == EnumAppSide.Client)
                {
                    handling = EnumHandHandling.PreventDefaultAction;
                    return;
                }

                if (blockSel == null)
                {
                    return;
                }

                ModSystemBlockReinforcement bre = byEntity.Api.ModLoader.GetModSystem<ModSystemBlockReinforcement>();

                IPlayer player = (byEntity as EntityPlayer).Player;
                if (player == null) return;


                if (player.WorldData.CurrentGameMode != EnumGameMode.Creative) { (player as IServerPlayer).SendIngameError("admin_nocreative", "You are not allowed to use this tool!"); return; }

                // Admin reinforcement Strength
                int strength = ADMIN_REINFORCE_STRENGTH;

                int toolMode = slot.Itemstack.Attributes.GetInt("toolMode");
                int groupUid = 0;
                var groups = player.GetGroups();


                // Reinforce to group
                if (toolMode > 0 && toolMode - 1 < groups.Length)
                {
                    groupUid = groups[toolMode - 1].GroupUid;
                }

                // Not reinforceable
                if (!api.World.BlockAccessor.GetBlock(blockSel.Position).HasBehavior<BlockBehaviorReinforcable>())
                {
                    (player as IServerPlayer).SendIngameError("notreinforcable", "This block can not be reinforced!");
                    return;
                }
                bre.ClearReinforcement(blockSel.Position);

                bool didStrengthen = groupUid > 0 ? bre.StrengthenBlock(blockSel.Position, player, strength, groupUid) : bre.StrengthenBlock(blockSel.Position, player, strength);

                if (!didStrengthen)
                {
                    (player as IServerPlayer).SendIngameError("alreadyreinforced", "Cannot reinforce block, it's already reinforced!");
                    return;
                }

                BlockPos pos = blockSel.Position;
                byEntity.World.PlaySoundAt(new AssetLocation("sounds/tool/reinforce"), pos.X, pos.Y, pos.Z, null);

                handling = EnumHandHandling.PreventDefaultAction;
                if (byEntity.World.Side == EnumAppSide.Client) ((byEntity as EntityPlayer)?.Player as IClientPlayer).TriggerFpAnimation(EnumHandInteract.HeldItemInteract);
            }



            public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
            {
                if (byEntity.World.Side == EnumAppSide.Client)
                {
                    handling = EnumHandHandling.PreventDefaultAction;
                    return;
                }

                if (blockSel == null)
                {
                    return;
                }

                ModSystemBlockReinforcement modBre = byEntity.Api.ModLoader.GetModSystem<ModSystemBlockReinforcement>();
                IServerPlayer player = (byEntity as EntityPlayer).Player as IServerPlayer;
                if (player == null) { return; }

                if (player.WorldData.CurrentGameMode != EnumGameMode.Creative) { player.SendIngameError("admin_nocreative", "You are not allowed to use this tool!"); return; }

                BlockReinforcement bre = modBre.GetReinforcment(blockSel.Position);
                
                if (bre == null) { return; } 


                if (bre.Locked)
                {
                    ItemStack stack = new ItemStack(byEntity.World.GetItem(new AssetLocation(bre.LockedByItemCode)));
                    if (!player.InventoryManager.TryGiveItemstack(stack, true))
                    {
                        byEntity.World.SpawnItemEntity(stack, byEntity.ServerPos.XYZ);
                    }
                }
                modBre.ClearReinforcement(blockSel.Position);

                BlockPos pos = blockSel.Position;
                byEntity.World.PlaySoundAt(new AssetLocation("sounds/tool/reinforce"), pos.X, pos.Y, pos.Z, null);

                handling = EnumHandHandling.PreventDefaultAction;
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
                var groups = forPlayer.GetGroups();
                SkillItem[] modes = new SkillItem[1 + groups.Length];
                var capi = api as ICoreClientAPI;
                int seed = 1;
                var texture = FetchOrCreateTexture(seed);
                modes[0] = new SkillItem() { Code = new AssetLocation("self"), Name = Lang.Get("Reinforce for yourself") }.WithIcon(capi, texture);
                for (int i = 0; i < groups.Length; i++)
                {
                    texture = FetchOrCreateTexture(++seed);
                    modes[i + 1] = new SkillItem() { Code = new AssetLocation("group"), Name = Lang.Get("Reinforce for group " + groups[i].GroupName) }.WithIcon(capi, texture);
                }

                return modes;
            }

            private LoadedTexture FetchOrCreateTexture(int seed)
            {
                if (symbols.Count >= seed) return symbols[seed - 1];

                var newTexture = GenTexture(seed, seed);
                symbols.Add(newTexture);
                return newTexture;
            }

            private LoadedTexture GenTexture(int seed, int addLines)
            {
                var capi = api as ICoreClientAPI;
                return capi.Gui.Icons.GenTexture(48, 48, (ctx, surface) => { capi.Gui.Icons.DrawRandomSymbol(ctx, 0, 0, 48, GuiStyle.MacroIconColor, 2, seed, addLines); });
            }


            //BIG PLUMB AND SQUARE, ADJENCY EXPANSION FUNCTION
            private List<BlockPos> getReinforceOrder(BlockPos middleBlock, EnumAxis blockAxis)
            {
                List<BlockPos> order = new List<BlockPos>();
                order.Add(middleBlock);



                return order;
            }

            public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
            {
                return interactions.Append(base.GetHeldInteractionHelp(inSlot));
            }
    }
}

