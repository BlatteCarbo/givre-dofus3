# Inventaire du mapping — 21/10/2026

Source : `data/fields.json` (3725 messages décrits) et `data/matching_current.json` (1337 messages avec code réseau).
Un champ « non nommé » est un champ dont le nom est obfusqué (4 lettres commençant par f ou g : `fzju`, `gccm`…) : son numéro est connu, pas son sens.
Le décompte est approximatif (heuristique sur le motif du nom).

## Compte & personnage (création, suppression, pseudo, secret)

13 message(s), 11 champ(s) non nommé(s).

| Message | Code | Champs nommés | Champs non nommés |
|---|---|---|---|
| CharacterCreationRequest | kru | modeling_information#1 | — |
| CharacterCreationResultEvent | ksn | result#1, reason#3 | fzju#2 |
| CharacterDeletionErrorEvent | kst | reason#1 | — |
| CharacterDeletionPrepareEvent | ksw | need_secret_answer#1, name#2, secret_question#3 | — |
| CharacterDeletionRequest | kso | character_id#2, secret_answer_hash#3 | fznb#1 |
| CharacterListWithRemodelingEvent | ksv | characters#1, characters_to_remodel#2 | fzid#3 |
| CharacterNameGenerationFailedEvent | ktf | reason#1 | — |
| CharacterNameSuggestionEvent | ksp | suggestion#1 | — |
| CharacterNameSuggestionRequest | ktb | — | — |
| CharacterReplayRequest | ksz | character_id#1 | fzir#2, fzit#3 |
| CharacterReplayWithRemodelRequest | krv | remodeling_information#1, character_id#2 | fzmw#4, fzmt#5 |
| CharacterSelectionWithRemodelRequest | ksq | remodeling_information#2, character_id#3 | fzke#1 |
| PrepareCharacterDeletionRequest | ksm | character_id#1 | fzly#2, fzlz#3, fzlx#5 |

## Boutique, ogrines, abonnement, Bourse

30 message(s), 19 champ(s) non nommé(s).

| Message | Code | Champs nommés | Champs non nommés |
|---|---|---|---|
| AccountSubscriptionElapsedDurationUpdateEvent | meq | subscription_elapsed_duration#1 | — |
| BakActionEvent | lpv | kamas#1, transaction_uuid#2, bid_action#3, amount#5, rate#6 | — |
| BakActionRequest | lpk | bid_action#1, rate#2, ogrines#3, kamas#4 | gcvp#6 |
| BakApiKeyEvent | lpp | token#1 | gcxj#2 |
| BakApiTokenRequest | lpj | — | — |
| BakBufferListEvent | lpf | buffers#1 | gcyx#3 |
| BakBufferListRequest | lpe | — | — |
| BakBuyValidationEvent | lpl | email#1, transaction_validation#2, amount#4 | gcxx#3 |
| BakCancelBidRequest | lpz | bid_cancellation_type#1, bid_id#2 | — |
| BakConsumeBufferRequest | lpi | — | gcwf#1 |
| BakShopTokenEvent | lps | token#1 | — |
| BakShopTokenRequest | lpw | — | — |
| BakTransactionValidationEvent | lpc | result#1, bid_action#2 | — |
| BakTransactionValidationRequest | lpo | transaction_uuid#2 | gcwj#1 |
| ExchangeMoneyMovementLimitEvent | kaq | limit#2 | fxqh#1 |
| ExchangeNpcShopStartedEvent | jzi | npc_seller_id#1, token_id#2, objects#3 | fxjw#4 |
| GuildShopOpenRequest | jie | — | — |
| GuildShopOpenResponse | jht | — | ftyk#1, ftyl#3 |
| HaapiAuthenticationErrorEvent | izm | — | — |
| HaapiSessionEvent | izn | session_uid#1 | fstu#2 |
| HaapiTokenEvent | izo | token#1 | — |
| HaapiTokenRequest | izp | — | fsui#1 |
| InfiniteDreamOpenShopEvent | ivd | — | fsal#1 |
| JobBookSubscriptionEvent | ipd | subscriptions#2 | frcw#1, frcx#3, frcv#5 |
| NpcShopUpdateBoughtItemEvent | ili | — | fqkc#1, fqkd#3, fqke#4 |
| RaidPurchaseRequest | htr | — | — |
| RaidPurchaseResponse | hwv | — | — |
| RaidPurchasedEvent | hvj | — | — |
| SubscriptionLimitationEvent | mef | reason#1 | — |
| SubscriptionZoneEvent | mdy | effective#2 | — |

## Échange & commerce entre joueurs / PNJ

88 message(s), 64 champ(s) non nommé(s).

| Message | Code | Champs nommés | Champs non nommés |
|---|---|---|---|
| ExchangeAcceptRequest | kax | — | — |
| ExchangeBidBuyerStartedEvent | jyt | selling_conditions#1 | fxeq#3, fxeo#4 |
| ExchangeBidHouseBuyRequest | kbf | quantity#1, object_uid#3, price#5 | — |
| ExchangeBidHouseBuyResultEvent | jzx | object_uid#2, bought#3 | fxbp#1 |
| ExchangeBidHouseGenericItemRemovedEvent | kdg | object_gid#2 | — |
| ExchangeBidHouseInListAddedEvent | jxv | effects#1, object_gid#2, object_type#3, prices#4, object_uid#5 | — |
| ExchangeBidHouseInListRemovedEvent | jyu | object_type#1, object_gid#2, object_uid#3 | fxeg#4 |
| ExchangeBidHouseInListUpdatedEvent | kas | prices#1, object_gid#2, object_type#3, object_uid#4, effects#5 | — |
| ExchangeBidHouseItemAddedEvent | jxt | price#1, item#2, unsold_delay#3 | — |
| ExchangeBidHouseItemRemovedEvent | jye | sell_id#1 | fxhv#2 |
| ExchangeBidHouseListRequest | kay | object_gid#1, follow#2 | — |
| ExchangeBidHouseOfflineSoldItemsEvent | jze | bid_house_items#1 | — |
| ExchangeBidHousePriceRequest | kap | object_gid#1 | fxcn#3 |
| ExchangeBidHouseSearchRequest | kcy | object_gid#1, follow#2 | — |
| ExchangeBidHouseTypeRequest | kcp | type_id#2, follow#3 | fxlr#1 |
| ExchangeBidHouseUnsoldItemsEvent | jzu | objects#1 | — |
| ExchangeBidPriceEvent | jyy | bid_price_for_seller#1, object_gid#2, average_price#3 | — |
| ExchangeBidSellerStartedEvent | kao | selling_conditions#1, objects#3 | — |
| ExchangeBoughtEvent | kbb | — | — |
| ExchangeBuyRequest | kby | — | fwwc#1, fwwd#2, fwwe#3 |
| ExchangeCraftCountModifiedEvent | jzg | count#1 | fxli#2 |
| ExchangeCraftCountRequest | kdj | count#2 | fxln#1 |
| ExchangeCraftPaymentModificationRequest | jzl | quantity#1 | — |
| ExchangeCraftPaymentModifiedEvent | kbv | kamas#1 | — |
| ExchangeCraftResultEvent | kco | result#1, object#2, object_gid#3 | — |
| ExchangeCraftStartedEvent | jyz | skill_id#1 | — |
| ExchangeCrafterJobLevelUpEvent | jyc | crafter_job_level#1 | fxbt#2 |
| ExchangeErrorEvent | kbi | error_type#1 | — |
| ExchangeFocusedReadyRequest | jxy | step#1, focus_action_id#2, ready#3 | — |
| ExchangeItemAutoCraftStoppedEvent | kce | reason#1 | — |
| ExchangeKamaModifiedEvent | kbm | quantity#1, remote#3 | fxmc#2 |
| ExchangeLeaveEvent | kbr | dialog_type#1, success#2 | — |
| ExchangeMountsStableAddedEvent | kbn | — | fxlx#1 |
| ExchangeMoveKamaRequest | jyj | quantity#1 | fxpj#3 |
| ExchangeMultiCraftCrafterCanUseHisResourcesEvent | kdu | — | fwzz#1, fwzy#2 |
| ExchangeMultiCraftCrafterStartedEvent | kdx | skill_id#2 | — |
| ExchangeMultiCraftCustomerStartedEvent | kbu | skill_id#1, crafter_job_level#2 | fxnu#4 |
| ExchangeMultiCraftOkEvent | kdp | role#1, initiator_id#3, other_id#4 | — |
| ExchangeMultiCraftSetCrafterCanUseHisResourcesRequest | kdz | allow#2 | — |
| ExchangeNpcTradeStartedEvent | jyp | npc_id#2 | — |
| ExchangeObjectModifiedInBagEvent | kab | remote#1, object#3 | fwqn#2 |
| ExchangeObjectModifyPricedRequest | jzt | quantity#1, object_uid#2, price#3 | — |
| ExchangeObjectMoveAndSetReadyRequest | jxu | quantity#2, gid#4 | fwsh#1, fwsk#3, fwsi#5 |
| ExchangeObjectMovePricedRequest | jyq | object_uid#1, quantity#2, price#3 | — |
| ExchangeObjectMoveRequest | jys | object_uid#1, quantity#3 | — |
| ExchangeObjectMoveToTabRequest | kdy | object_uid#2, quantity#3, tab_number#4 | fxhh#1, fxhm#6, fxhi#7 |
| ExchangeObjectPutInBagEvent | kat | remote#1, object#2 | fwxd#4, fwxe#5, fwxa#6 |
| ExchangeObjectRemovedEvent | kcx | remote#2, object_uid#3 | — |
| ExchangeObjectRemovedFromBagEvent | kdo | remote#1, object_uid#3 | fxca#2 |
| ExchangeObjectTransferAllFromInventoryRequest | kbs | — | fwro#1 |
| ExchangeObjectTransferAllToInventoryRequest | kaw | — | fwyc#1 |
| ExchangeObjectTransferExistingFromInventoryRequest | jyd | — | — |
| ExchangeObjectTransferExistingToInventoryRequest | kef | — | — |
| ExchangeObjectTransferListFromInventoryRequest | jyk | objects_uid#1 | — |
| ExchangeObjectTransferListToInventoryRequest | kbp | — | fxbd#1, fxbe#2, fxbf#3 |
| ExchangeObjectTransferListWithQuantityToInventoryRequest | kdq | objects#1 | — |
| ExchangeObjectTransferTypeFromInventoryRequest | kea | object_type#1 | — |
| ExchangeObjectUseInWorkshopRequest | kct | — | fwvp#1, fwvq#2 |
| ExchangeObjectsAddedEvent | kch | remote#1, fm_power#2, objects#3 | — |
| ExchangeObjectsModifiedEvent | kdr | remote#2, objects#3 | fwzm#1, fwzo#4 |
| ExchangeObjectsRemovedEvent | jxz | remote#1, objects_uid#2 | fxmm#4 |
| ExchangeObjectsSellRequest | kcb | objects#1 | fake#2 |
| ExchangePlayerMultiCraftRequest | jyr | exchange_type#1, target_id#2, skill_id#3 | fwsa#5 |
| ExchangePlayerRequest | jzf | target_id#1 | — |
| ExchangePodsModifiedEvent | jza | current_weight#1, remote#3, max_weight#4 | fxkq#2 |
| ExchangeReadyEvent | kec | ready#1, character_id#2 | fxfi#4 |
| ExchangeReadyRequest | kba | step#1, ready#2 | fxig#4 |
| ExchangeRecycleTradeStartedEvent | kbq | — | fwrt#1, fwru#2, fwrv#3, fwrs#4, fwrw#5 |
| ExchangeReplayStopRequest | jzy | — | — |
| ExchangeRequestedTradeEvent | kaz | exchange_type#1, source_id#2, target_id#3 | fwqf#5 |
| ExchangeSellRequest | jzp | — | fwzd#1, fwze#2 |
| ExchangeSetCraftRecipeRequest | kar | object_gid#1 | — |
| ExchangeSoldEvent | jyo | — | — |
| ExchangeStartedEvent | kdk | exchange_type#2 | fxaq#1, fxar#4 |
| ExchangeStartedWithMultiTabStorageEvent | kah | exchange_type#1, tab_number#2, storage_max_slot#3 | — |
| ExchangeStartedWithPodsEvent | kbc | second_character_current_weight#1, first_character_id#2, exchange_type#3, first_character_max_weight#4, first_character_current_weight#5, second_character_id#6, second_character_max_weight#7 | fwss#8 |
| ExchangeStartedWithStorageEvent | jyf | exchange_type#1, storage_max_slot#2 | fxna#3, fxmz#5 |
| ExchangeTaxCollectorEquipmentRequest | kai | tax_collector_uid#1 | fxmv#2 |
| ExchangeTaxCollectorEquipmentStartedEvent | kcg | tax_collector_information#1 | — |
| ExchangeTaxCollectorGetEvent | kda | caller_id#1, user_name#2, look#3, collector_name#4, coordinates#5, objects#6, caller_name#7, pods#8 | — |
| ExchangeTaxCollectorRequest | jyv | tax_collector_uuid#1 | — |
| ExchangeTranscendentConfirmationEvent | keb | — | fxkw#1 |
| ExchangeTranscendentConfirmationRequest | kdn | — | fxps#2 |
| ExchangeTranscendentCustomerCancellationEvent | kcz | — | — |
| ExchangeTypesExchangerDescriptionForUserEvent | kbj | type_description#1, object_type#2 | — |
| ExchangeTypesItemsExchangerDescriptionForUserEvent | jzs | item_descriptions#1 | fwtz#2, fwub#3 |
| ExchangeWeightEvent | keg | max_weight#1, current_weight#2 | fxdj#3 |
| PrismExchangeRequest | icq | — | — |

## HDV

2 message(s), 1 champ(s) non nommé(s).

| Message | Code | Champs nommés | Champs non nommés |
|---|---|---|---|
| BidActivitiesRequest | kxs | — | — |
| BidActivityResponse | kxt | — | gady#1 |

## Dialogue PNJ

15 message(s), 19 champ(s) non nommé(s).

| Message | Code | Champs nommés | Champs non nommés |
|---|---|---|---|
| DialogLeaveEvent | kis | dialog_type#1 | — |
| DialogLeaveRequest | kiq | — | — |
| LockableShowCodeDialogEvent | kfa | code_size#1, action#2 | fxtu#3 |
| NpcDialogCreationEvent | ilx | portal_type#1, map_id#3, npc_id#5 | fqnz#2 |
| NpcDialogQuestionEvent | ilf | visible_replies#1, dialog_params#2, message_id#3 | — |
| NpcDialogReplyRequest | iky | reply_id#1 | fqnb#2 |
| NpcGenericActionFailureEvent | ilm | — | — |
| NpcGenericActionRequest | ilt | npc_action_id#1, npc_map_id#2, npc_id#3 | — |
| NpcsMapQuestStatusUpdateEvent | ilr | map_information#1 | — |
| PlayAnimationOnNpcEvent | ikv | — | fqjy#1, fqjx#2 |
| PlayEmoteOnNpcEvent | ikw | — | fqmv#1, fqmw#2 |
| PlaySpellScriptOnNpcEvent | ikx | — | fqkr#1, fqks#2, fqku#3, fqkt#5 |
| PrismDialogQuestionEvent | ils | — | fqlp#1 |
| PurchasableDialogEvent | kew | action#3, purchasable_instance_id#5, second_hand#6 | fxtp#1, fxtq#2, fxtm#4 |
| TaxCollectorDialogQuestionEvent | iku | alliance_information#2, looted_items_value#6, possible_attack#7 | fqmk#1, fqml#3, fqmo#4, fqmq#5 |

## Chasse au trésor

11 message(s), 3 champ(s) non nommé(s).

| Message | Code | Champs nommés | Champs non nommés |
|---|---|---|---|
| TreasureHuntAnswerEvent | hfu | result#1, quest_type#2 | flyt#3 |
| TreasureHuntDigAnswerEvent | hfr | wrong_flag_count#1, quest_type#2, result#3 | — |
| TreasureHuntDigRequest | hes | quest_type#1 | flyn#2 |
| TreasureHuntEvent | hfn | flags#1, total_step_count#2, known_steps#3, total_check_point#4, quest_type#5, portal_info#6, current_check_point#7, available_retry_count#8, start_map_id#9 | — |
| TreasureHuntFinishedEvent | het | quest_type#1 | — |
| TreasureHuntFlagAnswerEvent | hfz | index#1, result#2, quest_type#5 | — |
| TreasureHuntFlagRemoveRequest | hfo | index#3, quest_type#4 | flxy#1 |
| TreasureHuntFlagRequest | her | index#2, quest_type#3 | — |
| TreasureHuntGiveUpRequest | heu | quest_type#1 | — |
| TreasureHuntLegendaryEvent | hex | available_legendary_ids#1 | — |
| TreasureHuntLegendaryRequest | hga | legendary_id#2 | — |

## Métiers, ateliers, craft

16 message(s), 8 champ(s) non nommé(s).

| Message | Code | Champs nommés | Champs non nommés |
|---|---|---|---|
| ChangeHighlightedJobsRequest | kus | — | fztd#1, fztc#2 |
| ChangeHighlightedJobsResponse | kvz | failed#1, success#2 | — |
| JobBookSubscribeRequest | kcf | jobs_id#1 | — |
| JobCrafterDirectoryAddEvent | ipf | entry#1 | fqzr#2, fqzs#3 |
| JobCrafterDirectoryListEvent | ioz | list_entries#2 | frbi#1 |
| JobCrafterDirectoryListRequest | iqd | job_id#1 | — |
| JobCrafterDirectoryOpenEvent | ipj | — | — |
| JobCrafterDirectoryRemoveEvent | iqg | job_id#1, player_id#3 | — |
| JobCrafterDirectorySettingsEvent | ipe | settings#1 | frby#2 |
| JobCrafterDirectorySettingsRequest | iqe | settings#2 | — |
| JobDescriptionEvent | ipg | jobs#1 | — |
| JobExperienceOtherPlayerUpdateEvent | ipp | player_id#1, experience#3 | — |
| JobExperiencesUpdateEvent | ipz | experiences#1 | frei#3, freg#4 |
| JobLevelUpEvent | ipa | new_level#1, job#2 | — |
| JobMultiCraftStateEvent | ipm | player_skills#1, enabled#2 | — |
| PaddockOpenedBySkillEvent | hou | paddock_id#2 | — |

## Groupe, amis, contacts

68 message(s), 48 champ(s) non nommé(s).

| Message | Code | Champs nommés | Champs non nommés |
|---|---|---|---|
| AcquaintanceAddedEvent | klo | acquaintance#1 | — |
| AcquaintanceListEvent | kmi | acquaintance#2 | fypa#1, fypc#3 |
| AcquaintanceListRequest | kmu | — | — |
| AddContactFailureEvent | klc | reason#1 | — |
| ContactLevelUpEvent | kkv | level#1, name#2, character_id#3 | fymp#4, fyms#5 |
| ContactWarnOnAchievementCompleteSetRequest | kkn | enable#1 | — |
| ContactWarnOnAchievementCompleteStateEvent | kmw | enable#1 | — |
| ContactWarnOnPermanentDeathSetRequest | klx | enable#1 | fylt#2 |
| FriendAddFailureEvent | kmd | reason#1 | — |
| FriendAddRequest | kln | target#1 | — |
| FriendAddedEvent | kma | friend#2 | — |
| FriendDeleteRequest | kml | account_id#2 | — |
| FriendDeletionResultEvent | kmf | success#2, account_tag#4 | — |
| FriendListEvent | kmt | friend#1 | — |
| FriendListRequest | kmk | — | — |
| FriendSetStatusShareRequest | kmj | share#1 | fyjp#2 |
| FriendSetWarnOnConnectionRequest | kkt | enable#1 | — |
| FriendSetWarnOnLevelGainRequest | klu | enable#1 | fyqh#3 |
| FriendStatusShareStateEvent | kmb | share#1 | — |
| FriendWarnOnConnectionStateEvent | kkq | enable#2 | — |
| FriendWarnOnLevelGainStateEvent | kku | enable#1 | fypk#3 |
| JoinFriendRequest | hlq | actor_id#1 | — |
| JoinSpouseRequest | hkz | — | fmyv#1 |
| PartyAbdicateThroneRequest | ijt | player_id#1, party_id#2 | fqgz#3 |
| PartyDeletedEvent | iir | party_id#1 | — |
| PartyFollowMemberRequest | ijo | player_id#1, enabled#2, party_id#3 | fqam#4 |
| PartyFollowStatusUpdateEvent | ihc | success#1, party_id#2, followed_id#3, is_followed#4 | — |
| PartyInvitationAcceptRequest | ijh | party_id#1 | fqbj#2 |
| PartyInvitationCancelRequest | ihj | guest_id#1, party_id#2 | — |
| PartyInvitationCancelledEvent | ijg | canceller_id#1, party_id#2, guest_id#3 | fqdz#5 |
| PartyInvitationCancelledForGuestEvent | iim | canceller_id#4, party_id#5 | — |
| PartyInvitationDetailsEvent | ijv | party_id#1, party_type#2, from_player_id#4, members#6, guests#8, leader_id#9 | fqgg#3, fqgh#5, fqgk#7, fqgl#11 |
| PartyInvitationDetailsRequest | iho | party_id#1 | — |
| PartyInvitationEvent | ije | party_type#1, max_participants#2, from_player_name#3, party_name#4, party_id#5, to_player_id#6, from_player_id#7 | — |
| PartyInvitationRefuseRequest | iif | party_id#1 | fqhg#3 |
| PartyInvitationRefusedEvent | iis | guest_id#1, party_id#3 | — |
| PartyInvitationRequest | iks | party_type#1, target#2 | — |
| PartyJoinErrorEvent | ihs | reason#3 | fqiy#1, fqiw#2, fqiz#4 |
| PartyJoinEvent | iiw | max_members#1, party_id#2, party_type#3, restricted#4, entity_member#5, leader_id#6, party_name#7, guests#9, members#10 | — |
| PartyKickEndedEvent | ihg | — | fqei#1, fqeh#2, fqeg#3 |
| PartyKickOnCooldownEvent | ikl | — | fpyb#1, fpyc#2 |
| PartyKickRequest | ihd | player_id#1, party_id#2 | — |
| PartyKickedByEvent | ihk | party_id#1, kicker_id#2 | fqim#4 |
| PartyLeaderUpdateEvent | ikn | party_leader_id#2, party_id#3 | fqcg#1 |
| PartyLeaveEvent | ihz | party_id#2 | fpvm#1 |
| PartyLeaveRequest | ihb | party_id#4 | fqew#1 |
| PartyLoyaltyStatusEvent | ikj | loyal#1, party_id#2 | — |
| PartyMemberInFightEvent | iia | fight_id#1, time_before_fight_start#3, member_name#4, member_id#5, standard_fight_map#6, party_id#7, reason#8 | fpyi#2, fpyk#10 |
| PartyMemberRemoveEvent | ikk | leaving_player_id#1, kicker_id#2, party_id#3 | fpvt#5 |
| PartyMemberUpdateEvent | iht | member#1, party_id#2 | fqhn#3 |
| PartyMemberUpdateLightEvent | iib | party_id#1, index#2, commons_information#3, player_id#4 | fqdk#6 |
| PartyNameSetErrorEvent | iiz | party_id#1, result#3 | — |
| PartyNameSetRequest | ihn | name#1, party_id#2 | fpxg#4 |
| PartyNameUpdateEvent | iki | party_id#1, name#2 | — |
| PartyNewGuestEvent | ihm | party_id#1, guest#2 | — |
| PartyNewMemberEvent | ijs | member#1, party_id#3 | fqfx#2, fqfz#4 |
| PartyNoKickRequest | ijz | — | — |
| PartyPendingKickEvent | ihl | — | fqdf#1, fqde#2, fqdg#3 |
| PartyPledgeLoyaltyRequest | iiq | loyal#1, party_id#2 | — |
| PartyRestrictedEvent | ijd | party_id#1, restricted#2 | — |
| PartyStopFollowMemberRequest | ijy | party_id#3, player_id#4 | — |
| PlayerFightFriendlyAnswerRequest | hmo | accept#1, fight_id#2 | — |
| PlayerFightFriendlyAnsweredEvent | hng | accept#3, fight_id#4 | fngr#1, fngs#2, fngq#5 |
| PlayerFightFriendlyRequestedEvent | hmt | fight_id#1, source_id#2, target_id#3 | fnei#5 |
| SpouseInformationEvent | hmb | gender#1, entity_look#2, character_name#3, guild#4, online#5, alliance#8, alignment#9, character_level#11, breed_id#12 | fmzj#6, fmzr#7 |
| SpouseInformationRequest | hkv | — | — |
| SpouseStatusEvent | hla | has_spouse#1 | — |
| SubscribeSpouseCompassRequest | hld | enable#1 | fmyl#2 |

## Guilde

129 message(s), 151 champ(s) non nommé(s).

| Message | Code | Champs nommés | Champs non nommés |
|---|---|---|---|
| BuyGuildBoostRequest | jhu | — | ftzn#1, ftzo#2, ftzp#3 |
| BuyGuildBoostResponse | jif | — | fuak#1, fual#2 |
| GuildApplicationAnswerEvent | jjj | accepted#1, guild_information#2 | fues#3 |
| GuildApplicationAnswerRequest | jjd | accepted#1, player_id#2 | — |
| GuildApplicationDeleteRequest | jjx | — | — |
| GuildApplicationDeletedEvent | jjf | deleted#2 | — |
| GuildApplicationGetOwnRequest | jjw | — | fudy#1 |
| GuildApplicationListenRequest | jjv | listen#1 | — |
| GuildApplicationModifiedEvent | jjt | player_id#1, state#2, application#4 | fudd#3 |
| GuildApplicationPlayerEvent | jjq | application#1 | — |
| GuildApplicationPresenceEvent | jjc | is_application#1 | fufm#2, fufn#4 |
| GuildApplicationPresenceRequest | jje | — | — |
| GuildApplicationReceivedEvent | jjn | player_id#2, player_name#4 | — |
| GuildApplicationSubmitRequest | jju | — | fufx#1, fufz#2, fufy#3, fuga#4, fugb#5, fugc#6, fugd#7, fuge#8, fugf#9, fugg#10, fugh#11, fugj#12, fugi#13 |
| GuildApplicationUpdateRequest | jjg | apply_text#1, guild_id#2 | — |
| GuildApplicationsEvent | jjy | total#2, applies#4 | fuez#1, fufb#3, fufd#6, fuey#7 |
| GuildApplicationsRequest | jjb | offset#1, count#2 | — |
| GuildBoostPricesRefreshEvent | jig | — | ftzt#2, ftzu#3 |
| GuildBoostStateRefreshEvent | jho | — | ftys#1, ftyr#2, ftyq#4 |
| GuildBulletinEvent | jgq | bulletin#3 | ftsp#2 |
| GuildBulletinSetErrorEvent | jfb | error#1 | ftqb#3 |
| GuildBulletinSetRequest | jgw | content#1 | — |
| GuildCardErrorEvent | jfw | guild_id#1 | — |
| GuildCardEvent | jgj | members#1, info#2, creation_date#3 | ftrl#5, ftri#6 |
| GuildCardRequest | jfg | guild_id#1 | — |
| GuildChestCurrentListenersAddEvent | jiw | players#1 | — |
| GuildChestCurrentListenersEvent | jiu | players#2 | fucf#1 |
| GuildChestCurrentListenersRemoveEvent | jiv | players#1 | — |
| GuildChestStructureStartListeningRequest | jiy | — | — |
| GuildChestStructureStopListeningRequest | jix | — | — |
| GuildChestTabSelectRequest | jiz | tab_number#1 | — |
| GuildChestTabUpdateRequest | jir | tab_number#1, drop_type_limitation#2, picto#3, name#4 | — |
| GuildContributeRequest | jim | — | fuaq#1, fuar#2 |
| GuildContributeResponse | jip | — | fuaw#1, fuay#2, fuav#3, fuax#4 |
| GuildContributionsLeftEvent | jil | — | fubc#1 |
| GuildCreationResultEvent | jgg | result#1 | ftuw#2 |
| GuildCreationStartedEvent | jfy | — | — |
| GuildCreationValidRequest | jfl | emblem#1, name#2 | — |
| GuildGeneralInformationEvent | jgi | creation_date#3, score#4, level#5, guild_kamas#6, exp_level_next_floor#8, members_count#9 | ftpq#1, ftpr#2, ftpu#7 |
| GuildHallAvailableThemesRefreshEvent | jpo | — | fuzj#1, fuzk#2 |
| GuildHallSelectRequest | jhj | — | — |
| GuildHallSelectResponse | jhg | — | ftwm#1 |
| GuildHallSelectedThemeRefreshEvent | jpa | — | fvbb#1, fvba#3 |
| GuildHallThemeSelectRequest | jhf | — | ftxe#1, ftxf#2 |
| GuildHallThemeSelectResponse | jhe | — | ftxm#1 |
| GuildHouseRemoveEvent | jhc | instance_id#1, second_hand#2, house_id#3 | — |
| GuildHouseUpdateEvent | jha | house#1 | — |
| GuildHousesEvent | jhb | houses#2 | — |
| GuildInformationRequest | jgm | information_type#1 | — |
| GuildInvitationAnswerRequest | jgf | accepted#1 | ftou#2 |
| GuildInvitationRequest | jff | target_id#1 | — |
| GuildInvitationStateRecruitedEvent | jfk | invitation_state#1 | fttf#2 |
| GuildInvitationStateRecruiterEvent | jge | recruited_name#2, invitation_state#4 | ftqp#3 |
| GuildInvitedEvent | jfe | guild_information#2, recruiter_name#3 | ftsu#1 |
| GuildJoinAutomaticallyRequest | jgy | guild_id#1 | ftuo#2 |
| GuildJoinedEvent | jeu | guild_information#1, rank_id#2 | — |
| GuildKamasEvent | jgn | kamas#2 | — |
| GuildKickRequest | jfh | kicked_id#2 | fttq#1, ftto#4 |
| GuildLeftEvent | jeq | — | ftmk#1 |
| GuildListenRequest | jfa | — | fttu#1, fttv#2 |
| GuildLogbookEvent | jgs | — | ftpg#1, ftph#2, ftpi#3, ftpj#4, ftpk#5 |
| GuildLogbookRequest | jft | — | — |
| GuildMemberLeaveEvent | jem | player_id#3 | ftky#1, ftla#2, ftkz#5 |
| GuildMemberOnlineStatusEvent | jew | member_id#1, online#2 | ftlp#3 |
| GuildMemberParametersChangeRequest | jeh | rank_id#2, member_id#3 | — |
| GuildMemberUpdateEvent | jep | member#1 | — |
| GuildMemberWarnOnConnectionStartRequest | jej | — | ftmc#1 |
| GuildMemberWarnOnConnectionStopRequest | jet | — | ftly#1 |
| GuildMembersEvent | jei | members#1 | ftnb#2 |
| GuildMembershipEvent | jev | guild_information#1 | ftmp#2, ftmq#3, ftmr#4 |
| GuildMissionCharacterClaimsRequest | jdl | — | — |
| GuildMissionCharacterClaimsResponse | jdp | — | fthq#1 |
| GuildMissionCharacterRewardsRequest | jdj | — | — |
| GuildMissionCharacterRewardsResponse | jdk | — | fthl#1, fthm#2 |
| GuildMissionCompleteEvent | jbq | — | fszy#1, fszz#2 |
| GuildMissionConfigurationRequest | jef | — | ftkf#1 |
| GuildMissionConfigurationResponse | jea | success#2 | ftjz#3 |
| GuildMissionConfigurationUpdateRequest | jee | — | ftjf#1, ftje#2 |
| GuildMissionConfigurationUpdateResponse | jdv | success#1 | ftif#2, ftig#4, ftih#5 |
| GuildMissionGeneratePoolRequest | jbr | — | ftde#1, ftdd#2, ftdf#3 |
| GuildMissionGeneratePoolResponse | jbw | success#2 | ftcm#1 |
| GuildMissionPoolRequest | jaq | — | fswy#1 |
| GuildMissionPoolResponse | jap | success#3 | fsya#1, fsyc#2, fsyb#5 |
| GuildMissionRerollRequest | jaf | — | fswp#1 |
| GuildMissionRerollResponse | jak | success#2 | ftax#1, ftaw#4 |
| GuildMissionSubscribeUpdateRequest | jat | — | ftaf#1, ftad#2, ftae#4 |
| GuildMissionSubscribeUpdateResponse | jbm | success#2 | ftbk#1 |
| GuildMissionUpdateEvent | jbp | — | ftaj#2 |
| GuildMissionUpdatePoolEvent | jar | — | ftbx#1, ftby#2 |
| GuildMissionViewStartListeningRequest | jdg | — | — |
| GuildMissionViewStartListeningResponse | jdf | — | — |
| GuildMissionViewStopListeningRequest | jde | — | — |
| GuildMissionViewStopListeningResponse | jdh | — | — |
| GuildModificationEmblemValidRequest | jgr | emblem#1 | ftnt#3, ftnr#4 |
| GuildModificationNameValidRequest | jfx | name#1 | — |
| GuildModificationResultEvent | jgv | result#1 | — |
| GuildModificationStartedEvent | jgh | can_change_emblem#1, can_change_name#3 | ftry#2 |
| GuildMotdEvent | jfo | motd#2 | — |
| GuildMotdSetErrorEvent | jfs | error#1 | — |
| GuildMotdSetRequest | jfr | content#1 | — |
| GuildNoteUpdateRequest | jfp | note#2, player_id#3 | ftvn#1 |
| GuildRaidShowRequest | hwm | — | — |
| GuildRaidShowResponse | hwb | — | — |
| GuildRankCreateRequest | jab | gfx_id#1, parent_rank_id#2, name#4 | fsvh#3 |
| GuildRankRemoveRequest | izz | new_rank_id#1, rank_id#2 | — |
| GuildRankUpdateAllRequest | izy | ranks#1 | fsvy#3 |
| GuildRankUpdateRequest | jaa | rank#2 | — |
| GuildRanksEvent | izx | ranks#2 | — |
| GuildRanksRequest | jac | — | fswj#1, fswk#2 |
| GuildRecruitmentEvent | izs | recruitment#2 | — |
| GuildRecruitmentInvalidateEvent | izt | — | fsuz#1 |
| GuildRecruitmentUpdateRequest | izr | recruitment#1 | — |
| GuildRightsUpdateRequest | jad | rights#2, rank_id#3 | fswe#1 |
| GuildSummaryEvent | jgx | guilds#1, count#2, offset#3, total#4 | ftue#7, ftuh#8 |
| GuildSummaryRequest | jgb | recruitment_type_filters#4, sort_type#6, name_filter#7, offset#12 | ftoc#1, ftof#2, ftog#3, ftoj#5, fton#8, ftoa#9, ftod#10, ftoo#11, ftoh#13, ftoi#14, ftop#16, ftom#17 |
| GuildUpdateNextWeeklyRequest | jcb | — | ftgi#1, ftgh#3 |
| GuildUpdateNextWeeklyResponse | jcp | success#4 | ftfp#1 |
| GuildWeeklyNextResetRequest | jcy | — | — |
| GuildWeeklyNextResetResponse | jcz | — | ftfa#1, ftfb#2 |
| GuildWeeklyRequest | jcs | — | — |
| GuildWeeklyResponse | jcj | success#2 | ftej#1, ftel#3, ftek#4 |
| GuildWeeklyRewardsAvailableEvent | jca | — | — |
| GuildWeeklyUpdateEvent | jcv | — | ftgd#1, ftgc#2 |
| HouseGuildNoneEvent | ixv | instance_id#1, second_hand#2, house_id#4 | — |
| HouseGuildRightsEvent | ixk | rights#1, instance_id#2, second_hand#3, house_id#4, is_locked#5, guild_information#6 | fskz#8 |
| HouseGuildRightsViewRequest | ixa | house_id#1, instance_id#2 | fsmg#3 |
| HouseGuildShareRequest | ixc | rights#1, share#2, instance_id#3, house_id#4 | — |
| RefreshGuildHallInfoEvent | jnq | — | fvbr#1, fvbq#2, fvbp#3 |
| SelectedGuildHallUpdateEvent | jhm | — | ftxq#2 |

## Alliance & prismes

80 message(s), 39 champ(s) non nommé(s).

| Message | Code | Champs nommés | Champs non nommés |
|---|---|---|---|
| AllianceApplicationAnswerRequest | lyc | accepted#1, player_id#2 | geeu#3 |
| AllianceApplicationDeletedEvent | lxq | success#1 | — |
| AllianceApplicationDeletionRequest | lyn | — | gegu#1 |
| AllianceApplicationInformationEvent | lxn | alliance#1, application_information#2 | gefi#4 |
| AllianceApplicationListEvent | lyi | applies#1, total#2, count#3, offset#4 | gegf#6 |
| AllianceApplicationListRequest | lxm | count#1, offset#2 | — |
| AllianceApplicationListenRequest | lyg | state#1 | gefc#3, gefa#4 |
| AllianceApplicationModifiedEvent | lym | apply#1, player_id#2, state#3 | — |
| AllianceApplicationNoInformationEvent | lyl | — | — |
| AllianceApplicationPresenceEvent | lyb | presence#1 | — |
| AllianceApplicationPresenceRequest | lxv | — | — |
| AllianceApplicationReceivedEvent | lyo | player_name#1, player_id#2 | geee#4 |
| AllianceApplicationRequest | lya | — | — |
| AllianceApplicationResponseEvent | lxu | accepted#1, alliance_information#2 | — |
| AllianceApplicationSubmitRequest | lyj | alliance_uid#1, text#2 | — |
| AllianceApplicationUpdateRequest | lyf | alliance_uid#1, text#3 | gecy#2 |
| AllianceBulletinEvent | mae | bulletin_info#1 | — |
| AllianceBulletinSetErrorEvent | mab | reason#1 | — |
| AllianceBulletinSetRequest | mad | content#1 | — |
| AllianceCreationResultEvent | mba | result#1 | — |
| AllianceCreationStartedEvent | mac | — | — |
| AllianceCreationValidRequest | lzz | alliance_name#1, alliance_emblem#2, alliance_tag#3 | — |
| AllianceEmblemModificationValidRequest | mau | alliance_emblem#1 | — |
| AllianceFactsEvent | maz | members#1, alliance_information#2, controlled_subarea_ids#3, leader_id#4, leader_name#6 | gepr#5 |
| AllianceFactsRequest | mal | alliance_uuid#1 | — |
| AllianceFightFighterAddedEvent | mbq | fight_information#1, team#2, fighter#3 | — |
| AllianceFightFighterRemovedEvent | mcm | fight_information#1, fighter_id#2 | gewl#4 |
| AllianceFightFinishedEvent | mbx | fight_information#1 | gewy#2 |
| AllianceFightJoinRequest | mbr | fight_information#1 | geut#2, geur#3 |
| AllianceFightLeaveRequest | mcd | fight_information#2 | — |
| AllianceFightListenStartRequest | mbp | — | — |
| AllianceFightListenStopRequest | mbt | — | — |
| AllianceFightPhaseUpdateEvent | mbw | phase#1, fight_information#2 | — |
| AllianceFightReplaceRequest | mbv | to_replace_fighter_id#1, fight_information#2 | geub#3 |
| AllianceFightStartedEvent | mbj | fight_information#1, phase#2 | geuy#4 |
| AllianceFightsInformationEvent | mbo | alliance_fights#1 | gews#2, gewr#4 |
| AllianceInsiderInformationEvent | mbc | members#1, prisms#2, tax_collectors#3, information#4 | geok#6 |
| AllianceInsiderInformationRequest | mas | — | — |
| AllianceInvitationAnswerRequest | lzj | accepted#2 | gelo#1, gelp#3 |
| AllianceInvitationRequest | lzf | character_id#1 | — |
| AllianceInvitationStateRecruitedEvent | lzn | invitation_state#1 | — |
| AllianceInvitationStateRecruiterEvent | lzg | recruited_name#1, invitation_state#2 | gejl#4 |
| AllianceInvitedEvent | lzp | recruiter_name#1, alliance_information#2 | geks#4, gekt#5, gekp#6 |
| AllianceJoinedEvent | lzo | alliance_information#1, rank_id#2 | — |
| AllianceKickRequest | lzk | kicked_id#1 | — |
| AllianceLeftEvent | lzl | — | — |
| AllianceMemberLeavingEvent | lzd | kicked#1, member_id#2 | gejr#4 |
| AllianceMemberStartWarningOnConnectionRequest | mao | — | geof#1 |
| AllianceMemberStopWarningOnConnectionRequest | lzv | — | — |
| AllianceMemberUpdateEvent | lzm | member#1 | — |
| AllianceMembershipEvent | lze | rank_id#2, alliance_information#3 | — |
| AllianceModificationResultEvent | mat | result#1 | — |
| AllianceModificationStartedEvent | mav | can_change_name#1, can_change_emblem#2 | gemb#3, gemc#4 |
| AllianceModificationValidRequest | lzy | alliance_name#1, alliance_emblem#2, alliance_tag#3 | — |
| AllianceMotdEvent | mbb | motd_info#1 | — |
| AllianceMotdSetErrorEvent | mak | reason#1 | — |
| AllianceMotdSetRequest | maf | content#1 | gemm#3, gemk#4 |
| AllianceNameAndTagModificationValidRequest | mag | alliance_name#1, alliance_tag#2 | gemq#4 |
| AllianceRankChangeRequest | lyx | rank_id#1, member_id#2 | geij#4 |
| AllianceRankCreationRequest | lyv | gfx_id#1, name#2, parent_rank_id#3 | — |
| AllianceRankDeletionRequest | lzb | replacement_rank_id#2, rank_id#3 | — |
| AllianceRankUpdateAllRequest | lyw | ranks#1 | geja#2 |
| AllianceRankUpdateRequest | lyq | rank#1 | — |
| AllianceRanksEvent | lyu | ranks#2 | — |
| AllianceRanksRequest | lyy | — | gehz#1 |
| AllianceRecruitmentAutomaticJoinRequest | lyk | alliance_uid#2 | — |
| AllianceRecruitmentInformationEvent | lxw | information#1 | — |
| AllianceRecruitmentInformationRequest | lyh | — | — |
| AllianceRecruitmentInvalidateEvent | lxr | — | — |
| AllianceRecruitmentUpdateRequest | lxz | information#2 | — |
| AllianceRightsUpdateRequest | lyt | rank_id#2, rights#3 | gehd#1, gehg#4 |
| AllianceSummaryEvent | maa | alliances#1, total#2, count#3, offset#4 | — |
| AllianceSummaryRequest | lzu | sort_descending#1, languages_filter#2, min_player_level_filter#3, criterion_filter#4, text_filer#5, following_alliance_criteria#6, offset#7, max_player_level_filter#8, filter_type#9, recruitment_type_filter#10, hide_full_filter#11, sort_type#12, count#13 | — |
| PrismAddOrUpdateEvent | icw | prism_localized_information#2 | fpdw#1 |
| PrismAttackRequest | icp | — | fpej#1 |
| PrismAttackResultEvent | ict | prism_localized_information#1, prism_attack_result#2 | — |
| PrismAttackedEvent | icm | prism_localized_information#2 | — |
| PrismListEvent | ico | prism_localized_information#2 | fpec#1 |
| PrismRemoveEvent | icl | sub_area_id#3 | — |
| PrismTeleportationRequest | icn | — | — |

## Montures & enclos

19 message(s), 12 champ(s) non nommé(s).

| Message | Code | Champs nommés | Champs non nommés |
|---|---|---|---|
| ActivatePaddockGaugeRequest | hoh | gauge#1 | fnrs#3 |
| ActivatePaddockGaugeResponse | hqe | success#2 | fnsb#1, fnsc#4 |
| ApplyPaddockGaugeBoostRequest | hog | boost_objects#1 | fntj#2 |
| ApplyPaddockGaugeBoostResponse | hpq | success#2 | fntb#1 |
| DeactivatePaddockGaugeRequest | hnv | gauge#2 | — |
| DeactivatePaddockGaugeResponse | hok | — | fniv#1 |
| MoveRidesToClosedPaddockResponse | hqk | — | — |
| OpenPaddockStateUpdateEvent | hny | rights#1 | — |
| PaddockAccessibilityRequest | hrz | — | fnrk#1 |
| PaddockAccessibilityResponse | hsu | activated_paddock#1 | fnir#3, fnip#4 |
| PaddockAccessibilityUpdateEvent | hsl | accessibility#1 | fnlo#2 |
| PaddockCloseRequest | hov | — | — |
| PaddockCloseResponse | hpb | — | fnvk#1 |
| PaddockClosedEvent | hss | — | fntn#1 |
| PaddockOpenRequest | hot | paddock_id#1, here#2 | — |
| PaddockOpenResponse | hpa | success#1, error#2 | — |
| PaddockUpdateEvent | hrf | paddock#1 | — |
| SimulatePaddockGaugeBoostRequest | hqr | boost_objects#1 | — |
| SimulatePaddockGaugeBoostResponse | hqq | success#1, unresolved_field192#2 | — |

## Havre-sac, maisons, coffres

48 message(s), 37 champ(s) non nommé(s).

| Message | Code | Champs nommés | Champs non nommés |
|---|---|---|---|
| HavenBagChangeRoomRequest | iyx | room_id#1 | — |
| HavenBagDailyLotteryEvent | ixz | result#1 | fsta#2, fstc#3, fste#4 |
| HavenBagEditCancelRequest | izf | — | — |
| HavenBagEditFinishedEvent | iyd | — | fsro#1 |
| HavenBagEditRequest | izi | — | — |
| HavenBagEditStartEvent | iyf | — | — |
| HavenBagEnterRequest | izg | owner#1 | — |
| HavenBagExitRequest | iyg | — | — |
| HavenBagFurnitureCloseRequest | iyy | — | — |
| HavenBagFurnitureEvent | iyp | furniture#1 | fsrf#2 |
| HavenBagFurnitureOpenRequest | iyt | — | — |
| HavenBagFurnitureRequest | izc | furniture#2 | — |
| HavenBagInvitationAnswerRequest | izh | accepted#2 | fspn#1 |
| HavenBagInvitationClosedEvent | iyc | host_information#2 | — |
| HavenBagInvitationEvent | iyh | guest_information#1, accepted#2 | — |
| HavenBagInvitationOfferedEvent | iyq | time_before_cancel#1, host_information#2 | — |
| HavenBagInvitationRequest | iyw | — | — |
| HavenBagKickRequest | iys | — | — |
| HavenBagPackListEvent | iye | packs_id#2 | fsph#1, fspi#3 |
| HavenBagPermissionsUpdateEvent | izb | permissions#1 | — |
| HavenBagPermissionsUpdateRequest | iyr | permissions#1 | — |
| HavenBagRoomUpdateEvent | iym | action#1, preview#3 | — |
| HavenBagThemeChangeRequest | iyo | theme#1 | fsqv#2 |
| HouseAccountEvent | ixd | houses#1 | fsil#2 |
| HouseBuyRequest | ixq | proposed_price#1 | fsiq#2, fsir#3 |
| HouseBuyResultEvent | ixp | price#2 | fsoo#1, fsoq#3, fsol#4, fson#5, fsop#6, fsor#7 |
| HouseInformationRequest | iwz | house_id#1, house_instance_id#2 | — |
| HouseInformationResponse | ixf | house_information#1 | fsjx#3 |
| HouseKickRequest | iwx | player_id#2 | fsnf#1, fsng#4 |
| HouseLockFromInsideRequest | ixb | code#1 | fsow#2 |
| HousePropertiesEvent | ixe | doors_on_map#1, properties#2, house_id#3 | — |
| HouseSellRequest | ixs | amount#1, is_inside#2, instance_id#3, for_sale#4 | fskf#6 |
| HouseSellingUpdateEvent | ixj | instance_id#1, house_id#2, buyer_account_tag#3, price#4, second_hand#5, buyer_account_nickname#6 | — |
| HouseTeleportRequest | ixi | instance_id#1, house_id#4 | — |
| HousesToSellEvent | ixo | page_index#1, houses#3, total_page#4 | fsof#2, fsog#6 |
| HousesToSellFiltersRequest | iwy | skill_requested#1, order_by#2, price_max#3, at_least_chest_number#4, area_id#5, at_least_room_number#7 | — |
| HousesToSellRequest | ixr | page_index#2 | — |
| LockableChangeCodeRequest | kfb | code#2 | — |
| LockableCodeResultEvent | kff | result#1 | fxsq#2 |
| LockableStateUpdateHouseDoorEvent | kfg | — | fxtd#1, fxte#2, fxtf#3, fxtg#4, fxth#5 |
| LockableUseCodeRequest | kfh | code#1 | fxsy#3 |
| MultiTabStorageEvent | iqo | tabs#1 | frki#3, frkg#4 |
| StorageInventoryContentEvent | irp | kamas#1, objects#2 | frsz#3 |
| StorageKamasUpdateEvent | ita | kamas#1 | frjw#2 |
| StorageObjectRemovedEvent | itf | object_uid#1 | — |
| StorageObjectUpdateEvent | iqz | object#1 | — |
| StorageObjectsRemovedEvent | isl | objects_uid#2 | frlh#1 |
| StorageObjectsUpdateEvent | itn | objects#1 | — |

## Quêtes

16 message(s), 11 champ(s) non nommé(s).

| Message | Code | Champs nommés | Champs non nommés |
|---|---|---|---|
| QuestObjectiveFollowRequest | ibv | quest_id#3, objective_id#4 | fozr#1 |
| QuestObjectiveUnfollowRequest | ibk | quest_id#1, objective_id#3 | — |
| QuestObjectiveUpdatedEvent | ibw | — | fpad#1, fpaf#2, fpae#4 |
| QuestObjectiveValidatedEvent | ibg | objective_id#1, quest_id#2 | — |
| QuestObjectiveValidationRequest | iay | objective_id#1, quest_id#2 | — |
| QuestResetEvent | ice | — | fpay#1, fpax#2 |
| QuestStartRequest | icf | quest_id#1 | fpbo#2 |
| QuestStartedEvent | icb | quest_id#1 | fozm#2 |
| QuestStepInformationEvent | icj | information#1, player_id#2 | — |
| QuestStepInformationRequest | ibj | quest_id#1 | fpbs#2 |
| QuestStepStartedEvent | ici | quest_id#3, step_id#4 | — |
| QuestStepValidatedEvent | ibe | quest_id#1, step_id#2 | — |
| QuestValidatedEvent | ibc | quest_id#1 | fpcd#2 |
| QuestsEvent | ica | reinitialized_done_quests_id#2, player_id#3, finished_quests#5, active_quests#6 | fpcr#4 |
| QuestsFollowedEvent | ibs | quests#2 | — |
| QuestsFollowedOrderRefreshRequest | ibb | quests#3 | — |

## Succès

21 message(s), 11 champ(s) non nommé(s).

| Message | Code | Champs nommés | Champs non nommés |
|---|---|---|---|
| AchievementDetailsEvent | mdv | achievement#1 | — |
| AchievementDetailsRequest | mdk | achievement_id#1 | — |
| AchievementFinishedEvent | mdi | achievement#3 | — |
| AchievementFinishedInformationEvent | mdh | player_name#1, achievement#2, player_id#3 | — |
| AchievementRewardRequest | mdw | achievement_id#1 | — |
| AchievementRewardResultEvent | mdj | achievement_id#1, success#3 | — |
| AchievementsAlmostFinishedDetailedEvent | mdu | almost_finished_achievements#1 | gfbs#2 |
| AchievementsAlmostFinishedDetailedRequest | mdt | — | — |
| AchievementsDetailedEvent | mdo | achievements#1 | geyz#3 |
| AchievementsDetailedRequest | mdm | category_id#1 | gezq#3 |
| AchievementsEvent | mdp | achieved_achievements#1 | — |
| AchievementsPioneerRanksRequest | mdn | — | — |
| AchievementsPioneerRanksResponse | mdg | achievements_pioneer_ranks#1 | — |
| LadderAchievementCurrentRequest | ioh | — | fqyz#1 |
| LadderAchievementCurrentResponse | iol | — | fqxx#1 |
| LadderAchievementSearchRequest | ioq | — | fqxt#1, fqxs#3 |
| LadderAchievementSearchResponse | iot | — | fqyc#1, fqyb#2 |
| LadderAchievementTopRequest | iop | — | fqxo#2 |
| LadderAchievementTopResponse | iom | — | fqyv#1 |
| RideAchievementObjectiveListEvent | hpe | — | — |
| RideAchievementObjectivesCompletedEvent | hrl | — | — |

## Almanax, anomalies, bonus

9 message(s), 8 champ(s) non nommé(s).

| Message | Code | Champs nommés | Champs non nommés |
|---|---|---|---|
| AlmanachCalendarDateEvent | lov | date_id#3 | gcuv#1, gcut#2 |
| AnomalyOpenedEvent | lxc | subarea_id#1 | — |
| AnomalyStateEvent | lwy | closing_time#1, open#2, subarea_id#3 | — |
| AnomalySubareaInformationEvent | lxb | subareas#1 | — |
| AnomalySubareaInformationRequest | lwx | — | — |
| BonusesEvent | kxn | — | gadk#1, gadl#2 |
| ChallengeBonusChoiceRequest | kua | challenge_bonus#1 | fzpx#2, fzpw#4 |
| ChallengeBonusChoiceSelectedEvent | ktx | challenge_bonus#1 | — |
| ObjectHarvestedWithBonusEvent | irh | bonus_quantity#1, object_gid#2, quantity#3 | friw#5, fris#6 |

## Kolizéum, arène, lobby

54 message(s), 23 champ(s) non nommé(s).

| Message | Code | Champs nommés | Champs non nommés |
|---|---|---|---|
| ArenaFightAnswerRequest | lqz | accept#1 | — |
| ArenaFightAnswerResponse | lsl | acknowledged#1 | — |
| ArenaFightPropositionEvent | lqp | duration#1, allies#2, fight_id#3 | gdet#5 |
| ArenaFighterIdleEvent | lrd | — | — |
| ArenaFighterStatusEvent | lqo | accepted#1, character_id#2, fight_id#3 | — |
| ArenaLeagueRewardsEvent | lsi | end_season_reward#1, season_id#2, ladder_position#3, league_id#5 | — |
| ArenaPlayerBehavioursEvent | lst | flags#1, sanctions#2, ban_duration#3 | — |
| ArenaRegisterRequest | lrc | arena_type#1 | gdaz#2 |
| ArenaRegistrationStatusEvent | lqy | step#1, registered#2, battle_mode#3 | — |
| ArenaRegistrationWarningEvent | lrf | battle_mode#1 | gdjw#3 |
| ArenaStateRequest | lre | — | — |
| ArenaStateResponse | lss | state#1 | gddd#2 |
| ArenaSwitchToFightServerEvent | lqv | ports#1, token#4, address#5 | gdjr#2 |
| ArenaUnregisterRequest | lqn | — | — |
| ArenaUpdatePlayerInformationEvent | lsh | ban_end_date#1, arena_ranks#2 | — |
| InfiniteDreamExitToLobbyRequest | ivc | — | frye#1 |
| InfiniteDreamExitToLobbyResponse | iur | — | fsct#1, fscu#2 |
| LadderArenaCurrentRequest | ioe | — | fqwr#1, fqws#2, fqwu#3 |
| LadderArenaCurrentResponse | iof | — | fqvs#1 |
| LadderArenaSearchRequest | iny | — | fqxe#1, fqwz#2, fqxd#3, fqxb#4 |
| LadderArenaSearchResponse | inx | — | fqwe#1 |
| LadderArenaTopRequest | iob | — | fqvw#1, fqwa#2, fqvy#3 |
| LadderArenaTopResponse | iod | — | fqxj#1, fqxi#2 |
| LobbyApplicationEvent | jna | — | — |
| LobbyApplyAnswerRequest | jkv | — | — |
| LobbyApplyAnswerResponse | jll | — | — |
| LobbyApplyCancelledEvent | jlw | — | — |
| LobbyApplyReceivedEvent | jmy | — | — |
| LobbyApplyResponse | jkt | — | — |
| LobbyApplyResultEvent | jld | — | — |
| LobbyBlacklistFreeEvent | jls | — | — |
| LobbyCardInactiveWhileApplyingEvent | jmz | — | — |
| LobbyCreationRequest | jki | — | — |
| LobbyCreationResponse | jkw | — | — |
| LobbyDeletedEvent | jlz | — | — |
| LobbyDeletionRequest | jkd | — | — |
| LobbyDeletionResponse | jlq | — | — |
| LobbyEditionRequest | jmd | — | — |
| LobbyEditionResponse | jly | — | — |
| LobbyFilterCardRequest | jmi | — | — |
| LobbyFilteredCardResponse | jmx | — | — |
| LobbyKickedEvent | jlx | — | — |
| LobbyLeaveRequest | jma | — | — |
| LobbyLeaveResponse | jlh | — | — |
| LobbyListRequest | jnb | — | — |
| LobbyListResponse | jmq | — | — |
| LobbyListenStopRequest | jnc | — | — |
| LobbyMemberJoinedEvent | jlm | — | — |
| LobbyMemberKickRequest | jme | — | — |
| LobbyMemberKickResponse | jkh | — | — |
| LobbyMemberLeftEvent | jms | — | — |
| LobbyOwnedResponse | jmf | — | — |
| ReadyToLeaveArenaRequest | lsk | — | — |
| ReadyToLeaveArenaResponse | lqs | — | gdao#1 |

## Chat & messages

11 message(s), 6 champ(s) non nommé(s).

| Message | Code | Champs nommés | Champs non nommés |
|---|---|---|---|
| ChatChannelMessageEvent | kpy | channel#1, date#2, sender_prefix#3, sender_name#4, from_admin#5, metadata#6, sender_account_id#7, sender_character_id#8, content#9, origin_server_id#10 | — |
| ChatChannelMessageRequest | kpw | channel#2, content#3, metadata#4 | fzbl#1, fzbq#5 |
| ChatChannelsEnabledEvent | kpv | active_channels#1, disabled_channels#2 | — |
| ChatErrorEvent | kqb | error#1 | fzcu#2 |
| ChatPrivateCopyMessageEvent | kqf | target_name#1, content#3, metadata#4, date#6, target_character_id#7 | — |
| ChatPrivateMessageRequest | kps | metadata#1, content#2, tag#3, name#5 | — |
| Comestic_Message_2 | lvq | — | — |
| GameMessage | hcn | response#1, event#3, request#4 | fmsc#2 |
| LivingObjectMessageEvent | imu | message_id#1, timestamp#2, owner_name#3, object_gid#4 | — |
| LivingObjectMessageRequest | imt | message_id#2, object_gid#3 | fqqs#1 |
| SystemMessageDisplayEvent | lor | hang_up#2, parameters#3, message_id#4 | gcuz#1 |

## Autres messages (hors catégories ci-dessus)

707 message(s) : AVAStateUpdateRequest, AccountAdditionalFacesEvent, AccountCapabilitiesEvent, AccountInformationUpdateEvent, AccountRightsEvent, ActivityHideRequest, ActivityLockRequest, ActivitySuggestionsEvent, ActivitySuggestionsRequest, AddRidesFromItemRequest, AddRidesFromItemResponse, AdditionalSmileyEvent, AggressionEvent, AlignmentRankUpdateEvent, AlreadyConnectedEvent, AlterationAddedEvent, AlterationRemovedEvent, AlterationsEvent, AlterationsUpdateEvent, AnimationTransitionEvent, ApplySpellModifierEvent, ApplyWeaponModifierEvent, AreaFightModifierUpdateEvent, AttackMonsterRequest, AuthenticationTicketAcceptedEvent, AuthenticationTicketRefusedEvent, AutoFollowActivatedEvent, AutoFollowActivationRequest, AutoFollowActivationResponse, AutoFollowDeactivatedEvent, AutoFollowDeactivationRequest, AutoFollowDeactivationResponse, BasicLatencyStatsEvent, BasicLatencyStatsRequest, BlockEvent, BlockListEvent, BlockListRequest, BlockRequest, BreedRideRequest, BreedRideResponse, BreedRideSimulationRequest, BreedRideSimulationResponse, COMMON_MESSAGE_1, COMMON_MESSAGE_2, COMMON_MESSAGE_3, COSMETIC_MESSAGE_1, COSMETIC_MESSAGE_2, CalendarEvent, CalendarSummaryRequest, CalendarSummaryResponse, CalendarWorldEvent, CanCharacterBeCreatedResultEvent, CanCreateCharacterRequest, CardInfoRequest, CardInfoResponse, CardOpenOutfitDraftRequest, CardOpenOutfitDraftResponse, CardViewRequest, CardViewResponse, CellPingEvent, ChallengeAddEvent, ChallengeListEvent, ChallengeModSelectRequest, ChallengeModSelectedEvent, ChallengeNumberEvent, ChallengeProposalEvent, ChallengeReadyRequest, ChallengeResultEvent, ChallengeSelectedEvent, ChallengeSelectionRequest, ChallengeTargetsEvent, ChallengeTargetsRequest, ChallengeValidateRequest, ChangeBackgroundCardRequest, ChangeBackgroundCardResponse, ChangeEnabledViewRequest, ChangeEnabledViewResponse, ChannelUpdateEvent, CharacterAppearanceCollectionEvent, CharacterAppearancesRequest, CharacterCapabilitiesEvent, CharacterCharacteristicUpgradeRequest, CharacterCharacteristicUpgradeResultEvent, CharacterCharacteristicsEvent, CharacterColorsUpdatedEvent, CharacterExperienceGainEvent, CharacterFillSlotColorsRequest, CharacterFillSlotColorsResponse, CharacterFirstSelectionRequest, CharacterForceSelectionEvent, CharacterForceSelectionReadyRequest, CharacterLevelUpEvent, CharacterLifeStatusEvent, CharacterListErrorEvent, CharacterListEvent, CharacterListRequest, CharacterLoadingCompleteEvent, CharacterOnConnectionEvent, CharacterPresetCreateRequest, CharacterPresetCreateResponse, CharacterPresetCurrentStateRequest, CharacterPresetCurrentStateResponse, CharacterPresetInfoRequest, CharacterPresetInfoResponse, CharacterPresetResetRequest, CharacterPresetResetResponse, CharacterPresetSetRequest, CharacterPresetSetResponse, CharacterRenameRequest, CharacterRestrictionsEvent, CharacterSelectionEvent, CharacterSelectionRequest, CharacterUpdateBreedRequest, CharacterUpdateColorsRequest, CharacterUpdateGenderRequest, CharacterUpdatedBreedEvent, CharacterUpdatedGenderEvent, CharacterUpdatedNameEvent, ChoiceCancelRequest, ChoiceCancelResponse, ChoiceSelectedRequest, ChoiceSelectionEvent, CinematicEvent, ClaimBoostRequest, ClaimBoostResponse, ClickEventRequest, ClickPingEvent, ClickStartListeningEvent, ClickStopListeningEvent, ClientChallengeInitRequest, ClientChallengeProofRequest, ClientIdRequest, ClientUIOpenedEvent, CloneRidesRequest, CloneRidesResponse, CloseChoiceSelectionEvent, CommunityChannelEvent, CompassResetEvent, CompassUpdateEvent, ConfigurationEvent, ConnectionEvent, ContextCreationEvent, ContextCreationRequest, ContextDestroyEvent, ContextKickRequest, ContextQuitRequest, ContextReadyRequest, ContextRefreshEntityLookEvent, ContextRemoveElementEvent, ContextRemoveElementsEvent, CosmeticInventoriesLoadedEvent, CosmeticInventoryAddObjectsRequest, CosmeticInventoryAddObjectsResponse, CosmeticInventoryAddedEvent, CosmeticInventoryPopObjectsRequest, CosmeticInventoryPopObjectsResponse, CurrentGlobalChoiceSelectionEvent, DateEvent, DateRequest, DebtsDeleteEvent, DebtsUpdateEvent, DebugClearHighlightCellsEvent, DebugHighlightCellsEvent, DebugInClientEvent, DecraftResultEvent, DelayedActionCancelRequest, DelayedActionEvent, DelayedActionFinishedEvent, DiceRollRequest, DocumentReadingBeginEvent, DumpedEntityStatsEvent, DungeonExitRequest, DungeonExitResponse, DungeonJoinMatchmakingRequest, DungeonJoinMatchmakingResponse, DungeonLeaveMatchmakingRequest, DungeonMatchmakingAcceptDungeonRequest, DungeonMatchmakingAcceptDungeonResponse, DungeonMatchmakingBanEvent, DungeonMatchmakingExitEvent, DungeonMatchmakingInformationRequest, DungeonMatchmakingInformationResponse, DungeonMatchmakingInvitationResultEvent, DungeonMatchmakingInvitationStatesEvent, DungeonMatchmakingJoinedEvent, DungeonMatchmakingRefuseDungeonRequest, DungeonSurrenderCooldownEvent, DungeonSurrenderEndedEvent, DungeonSurrenderPendingEvent, DungeonSurrenderRequest, EmoteAddedEvent, EmoteMassivePlayEvent, EmotePlayErrorEvent, EmotePlayEvent, EmotePlayRequest, EmoteRemovedEvent, EmotesEvent, EntitiesDispositionEvent, EntityPingEvent, EntityTalkEvent, Event, ExitRequest, ExtractRidesRequest, ExtractRidesResponse, FightAutoJoinActivatedEvent, FightAutoJoinActivationRequest, FightAutoJoinActivationResponse, FightAutoJoinDeactivatedEvent, FightAutoJoinDeactivatedResponse, FightAutoJoinDeactivationRequest, FightAutoReadyActivatedEvent, FightAutoReadyActivationRequest, FightAutoReadyActivationResponse, FightAutoReadyDeactivatedEvent, FightAutoReadyDeactivationRequest, FightAutoReadyDeactivationResponse, FightBoostAddEvent, FightBoostRemoveEvent, FightBoostUpdateEvent, FightBoostUpdateStateEvent, FightChallengeJoinRefuseEvent, FightChangeControllerEvent, FightChoiceSelectionEvent, FightControlEvent, FightEndEvent, FightEndKickedEvent, FightFighterRefreshEvent, FightFighterShowEvent, FightFreeControlEvent, FightHumanReadyStateEvent, FightIsTurnReadyEvent, FightJoinEvent, FightJoinRequest, FightJoinRunningEvent, FightLeaveEvent, FightMapInformationEvent, FightMapInformationResponse, FightMapRequest, FightNewRoundEvent, FightNewWaveEvent, FightOptionToggleRequest, FightOptionUpdateEvent, FightPauseEvent, FightPingCellRemoveRequest, FightPingCellRequest, FightPingEntityRemoveRequest, FightPingEntityRequest, FightPingsEvent, FightPingsRequest, FightPlacementPositionRequest, FightPlacementPossiblePositionsEvent, FightPlacementSwapPositionsAcceptRequest, FightPlacementSwapPositionsCancelRequest, FightPlacementSwapPositionsCancelledEvent, FightPlacementSwapPositionsErrorEvent, FightPlacementSwapPositionsEvent, FightPlacementSwapPositionsOfferEvent, FightReadyRequest, FightRefreshCharacterStatsEvent, FightRequestCanceledEvent, FightScenarioEvent, FightScoreInformationEvent, FightScoreUpdateEvent, FightSpectatePlayerRequest, FightSpectatorJoinEvent, FightSpellsUpdateEvent, FightStartEvent, FightStartingEvent, FightStatisticsEvent, FightSynchronizeEvent, FightTeamRemoveMemberEvent, FightTeamUpdateEvent, FightTurnEndEvent, FightTurnEvent, FightTurnFinishRequest, FightTurnReadyRequest, FightTurnStartPlayingEvent, FileCheckEvent, FileCheckRequest, ForgettableSpellActionRequest, ForgettableSpellDeletionEvent, ForgettableSpellEquipmentSlotsEvent, ForgettableSpellPresetCreateResponse, ForgettableSpellPresetInfoResponse, ForgettableSpellPresetResetRequest, ForgettableSpellPresetSetRequest, ForgettableSpellsEvent, FreeBlacklistedPlayerRequest, FreeBlacklistedPlayerResponse, FreeSoulRequest, GameActionAcknowledgementRequest, GameActionFightCastOnTargetRequest, GameActionFightCastRequest, GameActionFightEvent, GameActionItemConsumeAllRequest, GameActionItemConsumeRequest, GameActionItemConsumedEvent, GameActionItemListEvent, GameActionSpamEvent, GameActionUpdateEffectTriggerCountEvent, GameOverEvent, GameRolePlayShowActorsEvent, GetRideGeneralInfoResponse, GetRidesAsItemRequest, GetRidesAsItemResponse, GetStableRidesResponse, GlobalChoiceSelectedEvent, GlobalChoiceSelectedRequest, GlobalChoiceSelectionEvent, GlobalChoiceVoteEvent, GoldAddedEvent, GroupTeleportPlayerAnswerRequest, GroupTeleportPlayerClosedEvent, GroupTeleportPlayerOfferEvent, GuestLimitationEvent, GuestModeEvent, GuideModQuitRequest, GuideModReturnRequest, IdentificationRequest, IgnoreEvent, IgnoreRequest, InfiniteDreamBuyRequest, InfiniteDreamBuyResponse, InfiniteDreamDropTableRequest, InfiniteDreamDropTableResponse, InfiniteDreamEndEvent, InfiniteDreamEndFightUpdateEvent, InfiniteDreamEnterRequest, InfiniteDreamEnterResponse, InfiniteDreamExitEvent, InfiniteDreamHostInvitationRequest, InfiniteDreamInformationEvent, InfiniteDreamInvitationAnswerRequest, InfiniteDreamInvitationOfferEvent, InfiniteDreamMoveRequest, InfiniteDreamOpenHUBEvent, InfiniteDreamTeleportRequest, InfiniteDreamUseRollRequest, InfiniteDreamUseRollResponse, InteractiveElementUpdatedEvent, InteractiveMapUpdateEvent, InteractiveUseEndedEvent, InteractiveUseErrorEvent, InteractiveUseRequest, InteractiveUsedEvent, InventoryContentEvent, InventoryContentWatchEvent, InventoryUpdateMergedInventoryTypeRequest, InventoryWeightEvent, KOTHEndEvent, KOTHUpdateEvent, KamasUpdateEvent, LadderDreamCurrentRequest, LadderDreamCurrentResponse, LadderDreamSearchRequest, LadderDreamSearchResponse, LadderDreamTopRequest, LadderDreamTopResponse, LadderExperienceCurrentRequest, LadderExperienceCurrentResponse, LadderExperienceSearchRequest, LadderExperienceSearchResponse, LadderExperienceTopRequest, LadderExperienceTopResponse, LeaderFollowUpdateRequest, LeaderFollowUpdatedEvent, LeaderPositionEvent, LifePointsRegenBeginEvent, LivingObjectChangeSkinRequest, LivingObjectDissociateRequest, MapChangeOrientationEvent, MapChangeOrientationRequest, MapChangeRequest, MapComplementaryInformationEvent, MapCurrentEvent, MapCurrentInstanceEvent, MapErrorNotFoundRequest, MapFightCountEvent, MapInformationRequest, MapMovementCancelRequest, MapMovementConfirmRequest, MapMovementConfirmResponse, MapMovementEvent, MapMovementRefusedEvent, MapMovementRequest, MapObstacleUpdateEvent, MapRunningFightDetailsEvent, MapRunningFightDetailsExtendedEvent, MapRunningFightDetailsRequest, MapRunningFightStopListeningRequest, MapRunningFightsEvent, MapRunningFightsRequest, MapTeleportOnSameEvent, MimicryFreeRequest, MimicryFreeResponse, MimicryRequest, MimicryResponse, MonsterAngryAtPlayerEvent, MonsterNotAngryAtPlayerEvent, MoodUpdateEvent …
