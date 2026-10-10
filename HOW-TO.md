# Sending an email to lots of your customers

This tool sends **one separate email to each person** from your Outlook address. Everyone sees only their own
name, never anyone else's. Every email shows up in **Sent Items** like normal, and replies come back to your inbox.

## First time only
1. Open the MailMerge web address you were given and sign in with the username and password you were sent.
2. Click **Accounts > Add account**, give it a name, click **Save**, then **Sign in with Microsoft**.
3. Sign in with the shop's email address and click **Accept**. You come straight back to MailMerge.

## Every time
1. **Contacts:** click **Upload Excel / CSV** and choose your customer spreadsheet. The top row should be headings
   (like *First Name*, *Email*). Check the preview, choose which column is the email, and click **Import**. You can
   fix names, add people, or tick *Don't email* right in the table. New customers later? Upload again into the same
   list and only new people are added.
2. **Emails > New email:** pick the list, write the subject and message. Click a column name (like *First Name*)
   to put it in, so each person gets "Dear Sarah" instead of a generic hello. Add attachments if you like.
   To email only some contacts, use **Recipient rules** below **Send to**. Add a condition, choose any imported
   column, and choose **is yes / true** or another comparison. Choose **all (AND)** to require every condition
   (e.g. RSVP and SEND_EMAIL both true), or **any (OR)** to require at least one. Use one condition to check a
   single column. Nonmatching people are skipped automatically for this email; their Contacts checkboxes stay
   unchanged. The editor shows how many are excluded and how many emails are ready to send.
3. For special offers and newsletters, tick **Add an unsubscribe link at the bottom**.
4. **Check it:** click **Preview** to see the email for a real customer, then **Send me a test** and look at it in
   your inbox.
5. Then choose:
   - **Create drafts:** nothing is sent. A draft for every person appears in your Outlook **Drafts** folder so you
     can read and change them first.
   - **Send:** sends them one by one, slowly (about 20 a minute). You can **Pause** any time and carry on later.

## Good to know
- **Spreadsheet flags:** real Excel TRUE values and text such as yes, y, t, 1, x, ✓ and ✔ count as yes.
  FALSE, blanks and other values count as not true. Comparisons ignore case and surrounding spaces.
- **Updating from another spreadsheet:** when updating existing contacts, **Replace existing values with blank
  cells** is checked by default. Keep it checked when a blank send flag means no; uncheck it to keep the old
  value for blank cells. Columns absent from the file keep their existing values.
- **Missing rule columns:** if you delete a referenced column or choose a list without it, fix or remove the
  rule before sending. An unfinished rule cannot silently turn into sending to everyone.
- **You can close the page.** Sending carries on by itself. If an email shows **Check**, it might or might not have
  gone out: look in Sent Items, then open **View log** and press *It was sent* or *Retry*.
- **Someone asks to stop getting emails?** Add them under **Opt-outs & privacy**. They'll be skipped from then on,
  in every email. People who click the unsubscribe link are added automatically.
- **Someone asks you to delete their details?** Under **Opt-outs & privacy > Remove a person**, enter their email,
  check what's listed, and press **Remove everything**. If it mentions an unsent draft, delete that in Outlook's
  Drafts folder too.
- **Only email people who have agreed to hear from you.** New Zealand law requires that for promotional email,
  plus your shop's details and a way to say no (the unsubscribe link does this).
- After 90 days, the list of who got each email is cleared (the email itself stays, with its totals). Use
  **Download log** first if you need a record, or **Copy as new email** to send it again.
- Start with a small batch (10–20) the first time. Email from a shop mailbox shouldn't be a huge blast.
- Your usual Outlook signature isn't added automatically. Paste it at the bottom of the message.
- Want to practise? Add a **Test mode** account: nothing is really sent, and **Test outbox** shows what would have
  gone out.
