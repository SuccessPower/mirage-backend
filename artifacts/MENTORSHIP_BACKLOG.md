# Working backlog — mentorship / calendar / invites / navigation

Chunks in execution order. Tick as completed.

## Chunk 0 — paid mentorship + groups (DONE)
- [x] Mentor notified when their invite code is redeemed (ProfessionalInviteRedeemed)
- [x] Fix: mentors not seeing requests — inner join on Profiles dropped rows; SendRequest
      violated the unique (mentor, mentee) index on re-request → now Reopen()
- [x] Free vs paid mentee tiers; separate groups (posts / chat / meetings / roster / video)
- [x] Paid mentorship pricing + payout account + Paystack checkout
- [x] Mentors publish public events; host-neutral /events/{id}/register
- [x] Migration (enum defaults corrected to 1, not 0)
- [x] mirage-frontend: types, api, practice dashboard, group page, profile checkout
- [x] mirage-mobile: practice screen UI (models + service done)

## Chunk 1 — blocking bugs (DONE)
- [x] Already-a-mentee must not see "Request mentorship" (web + mobile)
- [x] Session expiry → immediate auto-redirect to signin; block all further API calls
- [x] Mentor gets an EMAIL for a new request — MentorRequestReceived had a template but was
      never in NotificationService.EmailableTypes, so no email was ever sent

## Chunk 2 — navigation + theme (DONE)
- [x] Mobile PWA: calendar in the top bar (icon-btn-mobile) as well as the tab bar
- [x] Web: theme toggle restored to the top-right (icon-btn-desktop), cycles auto/light/dark
- [x] Web: calendar removed from the desktop top bar; still in the profile dropdown
- [x] Default theme = system, on web (composables/useTheme.ts, applied before mount) and on
      Flutter (ThemeController now defaults to ThemeMode.system)
- [x] Profile settings theme control now shares the same composable, with an Auto option

## Chunk 3 — professional invite links (IN PROGRESS)
- [ ] Short descriptive codes (initials + digits)
- [ ] Invite link routes to SIGNUP (create the route if missing), not an empty page
- [ ] Signup + edit-profile: invite-code field
- [ ] Mentee's mentors hub: enter a mentor's code to send a request
- [ ] Shareable as QR / postcard
- [ ] Every request still subject to the professional's approval

## Chunk 4 — calendar + reminders
- [ ] Every scheduled meeting (mentor or counsellor) appears on BOTH sides' calendars
- [ ] Reminder in-app + email the day before, and 15 minutes before
- [ ] Events (community, dates, friends' events) marked on calendars + reminders

## Chunk 5 — design
- [ ] All internal hubs full-screen (like the pre-login landing page), not boxed

## Chunk 6 — verify
- [ ] Mentor + counsellor dashboards: chat and schedule calls with individuals, couples, groups
      (mentorship group; couple group for counselling)
