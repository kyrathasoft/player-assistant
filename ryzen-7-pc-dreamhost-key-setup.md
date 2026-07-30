- Wired default DreamHost target to the managed SSH alias in deploy scripts so workspace deploy flow now resolves through player-assistant-dreamhost.
  - Modified:
      - /C:/repos/player-assistant/web-deploy/deploy-pwa-files.ps1
      - /C:/repos/player-assistant/web-deploy/deploy-word-count-refresh.ps1
      - /C:/repos/player-assistant/web-deploy/publish-word-counts.ps1
      - /C:/repos/player-assistant/web-deploy/test-word-count-refresh-deployment.ps1

  - Deployment path flow now in practice:
      - PWA deploy scripts stage/install under /home/dh_4gg2za/bryanmiller.us/scarlethorizons/pwa
      - Broker/refresh deploy scripts use /home/dh_4gg2za/player-assistant-broker
      - Word-count source publish target is /home/dh_4gg2za/bryanmiller.us/scarlethorizons/data/word-counts.json

  - Existing SSH config block is already present and points the alias to:
      - Host player-assistant-dreamhost
      - HostName pdx1-shared-a1-13.dreamhost.com
      - User dh_4gg2za
      - IdentityFile C:/Users/Administrator/.ssh/dreamhost_player_assistant
