# Session Prompt

I want to make a small utility for personal use, on my version of Windows 11 Pro; you are to help me as an expert Windows application engineer and UI/UX designer.  I plan to post this project to GitHub as open source code (repo initiated), and compiled 'release' binaries, but with the caveat that I am not doing testing on any other versions of windows than mine.  This is a new project from a fresh start (the only file is this prompt file) for personal use, so we have no constraints due to legacy or backwards compatibility, nor do we need to fully consider other hardware/software baselines (though we should make the code modular and abstracted enough to easily update the code to support these later).

## Utility Goals

The goals of this utility include:

- Enable saving of current Windows display and sound settings as a `Profile`
  - To keep this simple, I only plan on saving current settings to a `Profile`, not on editing profiles which would be more UI effort than I want (at least currently)
  - It would be nice to have a Windows debug log which prints settings on save (and on activation), but we do not need any active listing of these in the utility's UI
  - A candidate storage location for modern windows is `C:\Users\<YourUsername>\AppData` but I am open to recommendations based on modern best practices in Windows
- Assignment of saved `Profile` to a hotkey
  - The hotkey must be able to be used without explicitly being inside the utility application.  Ideally, we would isolate the interaction to only when desktop or windoes toolbars are active to avoid compatibility issues with applications and games
  - I am thinking of using F9-F12 by default, or F5-F8; if it is not too costly, I would like to be able to 'map' a hotkey from a user-input sequence (I.e. might want function+key, ctrl+alt+key); I do not have ideas how to deconflict these with other standing windows keys, but we should look into that if we go a 'mapping' route.
  - I would like you to do some research on how to implement and provide recommendations
  - As an alternate
- Activation of a `Profile` via pressing the hotkey (keyboard, and also selectable in UI/Menu)
  - This hotkey likely needs some kind of passive listener/handler that should cost very little in terms of system resources; if this can be made completely passive, this may impact design
  - `Profile` activation should:
    - Switch duisplay settings and sound output device to those in the selected profile
    - Produce a sound on the new/selected sound device
    - Produce a visual notification (in the system tray or Windows notifications)
- `Profile` maintenance
  - Deletion of a `Profile` (with confirmation popup)
  - Renaming of a `Profile`

## Background and Motivation

I currently have a single PC that feeds several profiles:

- multi-monitor 'work' setup that uses the attached PC speakers
- single monitor/TV 'couch gaming' setup with the TV's (optical out) connected soundbar
- single-monitor 'desk gaming' using the attached PC speakers

I have managed to get all three working without connecting/disconnecting from my PC's video card, but switching between the profiles requires several switches:

- powering-on/off devices
- switching main display
- switching sound device

### Isuses and Pain Points

- The Windows settings UI sometimes gets stuck on a different window which has either been turned off or is difficult to view from my physical location since one of the displays is physically separated from the others
  - A hotkey which does not require confirmation could help fix this
- Selecting the sound device (usually to go with the main display) is tedious and the built-in Windows UIs are unresponsive (i.e. volume checking).
  - We want our switch to have a notification sound which activates on the new/selected sound device
- `System Sounds` is sometimes treated as an application, i.e. not switched by default in Volume Mixer, leading to a potential difference between application and system sounds
  - We want **all** applications and system sounds to be aligned to the selected device on profile selection
- I tend to leave all displays connected in all profiles since switching this is time-consuming.  As a result, I sometimes have display hardware powered off because I was in or am transitioning to a different profile, then when I intend to switch modes, the displays or cursor may be on or traverse through 'active' displays which are turned off making settings updates harder.  I would rather be able to disconnect the displays more easily (single hotkey to a `Profile`), which allows the physical monitors to instead go to low-power mode and re-activated to normal power mode when they become re-activated as part of setting a new `Profile`.

## Attributes to save and manipulate

I need the settings profiles to be able to ((save current)) / ((activate as current)) for...

### Display Settings

- Display configuration (available in Windows under Settings: `System` > `Display`) including the configuration, setting for each display as (A) `Extend desktop to this display` (B) 'duplicate desktop' on this display (C) `Disconnect this display`, as well as the `Make this my main display` option (One and only one display can get this at a time).  
  - We can assume a multiple display setup.  
  - I am not totally sure how Windows uses the `Remember window locations base on monitor connection` checkbox option, but I tend to have it checked in most instances, and it may come into play for this utility.
  - Scale, Display resolution, and Display Orientation may be stretch goals, but not primary goals
  - If Windows exposes other related settings to the API we should review these in planning

### Sound Settings

- The `Device` as in the Volume Mixer or the `Sound output` > `Oputput device`.  Since these lists are the same, and I can select the device in either UI, I think these map from the core Windows functionality, but I don't know easiest way to manipulate these programatically.  I Like the layout of volume mixer since the device and volume slider (which allows testing the device) are on the same page.

## User Interface

I am currently envisioning the utility as a system tray icon, with a menu available on click, but open to other designs -- please present a few alternates in interactive conversation.  The goal is to be computationally efficient with the program when not in use (ideally inactive) but able to have its functionality called with a single keypress (i.e. from basic Desktop, not after actively interacting with other menus to get to that intent).  We can limit scope somewhat if this helps deconflict, such as the application may only be active/listening when the windows desktop is active/in focus, or other windows menus or dialogs are active / in focus.

## Code Architecture

I want to discuss the code architecture a bit.  Although I don't imagine this will have many dependencies or imports, I still want a repeatable build system, and I would prefer to code in VS Code (i.e. over Visual Studio) since that is where I do my other coding.  I have been doing Unity development, so I likely have all the windows libraries needed even to handle `.csproj` builds already installed, though I am not sure how to orchestrate these outside Unity.  Also, I do have WSL, but I do not envision we will need this since we are working in windows and intending to make a windows application.  The application MUST be capable of installing and un-installing cleanly (including removal via Windoes Settings: `Apps` > `Installed apps`); removal should remove all user-specific generated files even in the app's local storage.

## Additional guidance

You are to present options in interactive conversation.  You should investigate current best practices with web searches since the human developer's programming experience is mainly linux-based.  Although we are starting from a fresh slate with no files, you should always read project files, rather than assuming things about them or that they have not changed since the last prompt.  You may use git commands to check file status, but you are to avoid doing commits and pushes as the human developer does these as part of quality control.  Make a CLAUDE.md file as part of the plan since I intend to use agentic coding for the implementation of this project.  You should actively recommend any Claude Code skills for repetitive tasks or other things that can be leveraged locally to prevent needlessly consuming context with these types of tasks.
