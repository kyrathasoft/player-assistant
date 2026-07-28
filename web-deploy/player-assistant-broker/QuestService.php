<?php

declare(strict_types=1);

final class QuestService
{
    private const VISIBILITY_VALUES = [
        'individual-only',
        'party-only',
        'individual-or-party',
    ];

    private const STATE_VALUES = [
        'open',
        'active',
        'abandoned-so-open',
        'completed',
        'withdrawn',
    ];

    private const QUESTS = [
        [
            'id' => 'find-jelenneth',
            'title' => 'Find Jelenneth or Learn Her Fate',
            'summary' => 'Search for Jelenneth, the missing daughter of Parker and Heldrith Laneworth. Rescue her, or learn her fate so her family can have closure.',
            'quest_giver' => 'Parker and Heldrith Laneworth',
            'visibility' => 'individual-or-party',
            'state' => 'active',
            'objectives' => [
                'Find and rescue Jelenneth, or learn what happened to her.',
                'Return reliable news to her family.',
            ],
            'reward' => '',
            'accepted_on' => 'May 6, 406 PR',
            'expires_on' => '',
            'wiki_url' => 'https://publish.obsidian.md/scarlethorizons/Quests/Find+Jelenneth+or+Learn+Her+Fate',
            'character_keys' => [],
        ],
        [
            'id' => 'three-items-for-nuanda',
            'title' => 'Three Items for Nuanda Nemere',
            'summary' => 'Retrieve the three ritual ingredients Nuanda Nemere requested and bring them to her before she no longer needs them.',
            'quest_giver' => 'Nuanda Nemere',
            'visibility' => 'individual-or-party',
            'state' => 'active',
            'objectives' => [
                'Obtain an amethyst worth at least 50 gp.',
                'Obtain hair from a deceased orc.',
                'Obtain Hwyanthemon.',
            ],
            'reward' => '800 XP each for up to four eligible PCs',
            'accepted_on' => '',
            'expires_on' => 'October 7, 406 PR',
            'wiki_url' => 'https://publish.obsidian.md/scarlethorizons/Quests/Quest+3+Items+for+Nuanda+Nemere',
            'character_keys' => [],
        ],
        [
            'id' => 'k-r-k-caravan-run',
            'title' => 'The K-R-K Caravan Run',
            'summary' => 'Guard Billworth Turgen\'s caravan from Kirkilston to Raven\'s Pass and see its cargo safely delivered.',
            'quest_giver' => 'Billworth Turgen',
            'visibility' => 'party-only',
            'state' => 'completed',
            'objectives' => [
                'Protect the caravan and its travelers.',
                'Deliver the surviving wagons to Raven\'s Pass.',
            ],
            'reward' => '15 gp each, a meal, and Billworth\'s offer of future work',
            'accepted_on' => '',
            'expires_on' => '',
            'wiki_url' => 'https://publish.obsidian.md/scarlethorizons/Quests/Quest+Caravan+Kirkilston+to+Raven\'s+Pass',
            'character_keys' => [],
        ],
        [
            'id' => 'plumb-lost-caverns',
            'title' => 'Plumb the Depths of the Lost Caverns',
            'summary' => 'Explore the rediscovered Lost Caverns of Achaia on behalf of the mage Delk Whitewand.',
            'quest_giver' => 'Delk Whitewand',
            'visibility' => 'party-only',
            'state' => 'open',
            'objectives' => [
                'Confirm and enter the Lost Caverns of Achaia.',
                'Explore the caverns and recover their lost secrets.',
                'Give any recovered scrolls and tomes to Delk.',
            ],
            'reward' => '100 gp up front, 10 gp daily, useful potions, and other recovered treasure',
            'accepted_on' => '',
            'expires_on' => '',
            'wiki_url' => 'https://publish.obsidian.md/scarlethorizons/Quests/Quest+Plumb+the+Lost+Caverns+of+Achaia',
            'character_keys' => [],
        ],
        [
            'id' => 'reclaim-keep-on-borderlands',
            'title' => 'Reclaim the Keep on the Borderlands',
            'summary' => 'Clear the goblins and bugbears from the abandoned keep that may stand over the ancient dwarf-hold Kharaz-Ankor.',
            'quest_giver' => 'Nolo Silvershaper',
            'visibility' => 'party-only',
            'state' => 'open',
            'objectives' => [
                'Clear the keep of its goblin and bugbear occupants.',
                'Investigate the possible entrance to the ancient dwarf-hold.',
            ],
            'reward' => '25 gp daily for the party for up to 14 days, plus 5 gp per goblin tongue',
            'accepted_on' => '',
            'expires_on' => '',
            'wiki_url' => 'https://publish.obsidian.md/scarlethorizons/Quests/Quest+Reclaim+the+Keep+on+the+Borderlands',
            'character_keys' => [],
        ],
        [
            'id' => 'construct-darkforest-fort',
            'title' => 'Help Reglar Ashbow Construct a Fort in the Darkforest',
            'summary' => 'Help Reglar Ashbow construct a fort on the eastern shore of Donally Lake in the Darkforest.',
            'quest_giver' => 'Reglar Ashbow',
            'visibility' => 'individual-or-party',
            'state' => 'open',
            'objectives' => [
                'Meet Reglar Ashbow and agree upon the work.',
                'Travel to the eastern shore of Donally Lake.',
                'Help construct and secure the fort.',
            ],
            'reward' => '',
            'accepted_on' => '',
            'expires_on' => '',
            'wiki_url' => 'https://publish.obsidian.md/scarlethorizons/NPCs/Reglar+Ashbow',
            'character_keys' => [],
        ],
        [
            'id' => 'find-urvan-and-narinza',
            'title' => 'Find Urvan and Narinza',
            'summary' => 'Determine what happened to Urvan and Narinza after they were separated from the party during the flight from Blightstone Pit.',
            'quest_giver' => 'Shade and the party',
            'visibility' => 'individual-or-party',
            'state' => 'active',
            'objectives' => [
                'Follow signs of the missing companions from Blightstone Pit.',
                'Find Urvan and Narinza, or learn their fates.',
            ],
            'reward' => '',
            'accepted_on' => '',
            'expires_on' => '',
            'wiki_url' => 'https://publish.obsidian.md/scarlethorizons/Meta/IC/Chapter+5+-+A+Betentacled+Escape',
            'character_keys' => [],
        ],
        [
            'id' => 'free-slaytonthorpe',
            'title' => 'Free Slaytonthorpe from the Terror of Red Tusk',
            'summary' => 'Fulfill Cromm\'s command to Neria by discovering the nature of Red Tusk and ending the terror afflicting Slaytonthorpe.',
            'quest_giver' => 'Cromm, through Neria Silverdale',
            'visibility' => 'individual-or-party',
            'state' => 'active',
            'objectives' => [
                'Learn who or what Red Tusk is.',
                'Travel to Slaytonthorpe.',
                'Free Slaytonthorpe from Red Tusk\'s terror.',
            ],
            'reward' => '',
            'accepted_on' => '',
            'expires_on' => '',
            'wiki_url' => 'https://publish.obsidian.md/scarlethorizons/Meta/IC/Chapter+6+-+At+the+Mason\'s+Apron',
            'character_keys' => [],
        ],
        [
            'id' => 'investigate-cold-mouth',
            'title' => 'Investigate the Cold Mouth and the Deep Friends',
            'summary' => 'Investigate the Cold Mouth named in the recovered Toothbreaker orders and identify the Deep Friends collecting captives there.',
            'quest_giver' => 'Recovered Toothbreaker orders',
            'visibility' => 'party-only',
            'state' => 'open',
            'objectives' => [
                'Locate the Cold Mouth north of Slaytonthorpe.',
                'Identify Red Tusk and the Deep Friends.',
                'Discover what happened to the captives taken there.',
            ],
            'reward' => '',
            'accepted_on' => '',
            'expires_on' => '',
            'wiki_url' => 'https://publish.obsidian.md/scarlethorizons/Writings/Scroll+Recovered+From+An+Orc+Near+Rimbling+Brook',
            'character_keys' => [],
        ],
    ];

    public function forAccount(array $account): array
    {
        $characterKey = strtolower(trim((string)($account['character_key'] ?? '')));
        $isDungeonMaster = (string)($account['role'] ?? '') === 'dm';
        $visible = array_values(array_filter(
            self::QUESTS,
            static function (array $quest) use ($characterKey, $isDungeonMaster): bool {
                if (!in_array($quest['visibility'], self::VISIBILITY_VALUES, true)
                    || !in_array($quest['state'], self::STATE_VALUES, true)) {
                    throw new RuntimeException('The configured quest has an invalid status.');
                }
                return $isDungeonMaster
                    || $quest['visibility'] !== 'individual-only'
                    || in_array($characterKey, $quest['character_keys'], true);
            }));

        return [
            'schema_version' => 1,
            'status_values' => array_merge(self::VISIBILITY_VALUES, self::STATE_VALUES),
            'quests' => array_map(
                static function (array $quest): array {
                    unset($quest['character_keys']);
                    return $quest;
                },
                $visible),
        ];
    }
}
