# Trust is Free, Spam Costs CRC

Let's face it: messaging in web3 is still kind of a disaster. Spam everywhere, random crypto bros shilling NFTs, "VCs" asking for a quick chat about your whitepaper… It’s a mess.

So, we thought: What if you could actually make strangers pay for your attention?

And thus, a slightly chaotic but surprisingly elegant experiment was born: **Paid DMs for Circles 2.0**.

*(Disclaimer: This probably won't solve spam forever, but it'll definitely make spammers reconsider their life choices.)*

## The Idea: Pay-per-message meets crypto trust circles

Circles 2.0 just rolled out a shiny new decentralized web-of-trust system. In short: you only directly receive tokens (CRC) from people you explicitly trust; everyone else’s tokens have to find their way through your circle of friends. We thought—why not use the same approach for messaging?

Here's how it works, in a nutshell:

* **Direct friends:** Message for free. You're in my circle? You're good. Slide in my DMs anytime.

* **Friends-of-friends and strangers:** You pay. The further away you are, the more expensive it gets.

You set your price table publicly, something like this:

| Distance               | Price                                           |
| ---------------------- | ----------------------------------------------- |
| Direct friend (0 hops) | Free  🎉                                        |
| 1 hop                  | 50 CRC                                          |
| 2 hops                 | 250 CRC                                         |
| 3+ hops                | 1000 CRC *(for serious inquiries only, please)* |

*A small fee goes to the dApp (gotta pay the bills), but most of the CRC goes straight to you. Cha-ching!*

## But Why?

Good question. Glad you asked.

We’re not really trying to "end spam forever." Let's be real: spam is eternal. Like death and taxes, spam will always find a way. Instead, we're playing with something much more fun: forcing spam to fund your coffee addiction.

Because if someone's gonna send unsolicited crypto investment advice or ask if you're "open to revolutionary DeFi yield opportunities," they might as well pay for your daily latte.

## How It Actually Works (the mildly nerdy part)

Circles 2.0 already gives you a decentralized profile system and cryptographic signing out-of-the-box. Each profile can store custom "namespaces" that live on IPFS (fully decentralized storage). You sign messages with your Ethereum wallet (either directly with your private key or through a Gnosis Safe). It's secure, simple, and surprisingly flexible.

All we did was add these extra pieces to your profile:

* A publicly readable "Pricing" JSON file (stored on IPFS) stating exactly how expensive your attention is.
* Messages themselves are also JSON payloads signed cryptographically, and optionally encrypted.
* Payments (CRC tokens) and receipts are attested cryptographically, with everything pinned on IPFS.

There's no central database. Just you, your wallet, your (expensive) attention, and your friends (who get free passes).

## Example: A crypto stranger tries to pitch you their token

1. **They check your price.** They find out they're 2 hops away and the price is 250 CRC. Ouch.
2. **They pay the fee.** Their CRC flows through your friend circle (trust paths!), you get most of the CRC, the operator gets a small tip. Transaction done.
3. **Message delivered.** You see a cryptographically verified receipt, proving they've paid. You decide whether to reply or simply enjoy your new crypto riches.

*(Pro tip: If you set your 3+ hops pricing high enough, spam becomes a legitimate side hustle.)*

## Human relays: because why automate when you can complicate?

Feeling generous? Want to help a friend-of-a-friend who doesn't have enough CRC to reach their crush? You can manually forward their message, personally signing and vouching for it—old-school relay style.

No guarantees it'll work, but hey, it's blockchain-powered matchmaking. Who doesn't love unnecessary complexity?

## Okay, but seriously—why?

Because crypto is supposed to be fun. Not every decentralized experiment needs to "change the world." Sometimes, you just want a cool new way to make your friends laugh—or better yet, make your acquaintances pay.

But joking aside, it’s a nice proof-of-concept to show off how Circles 2.0, IPFS, and simple cryptography can be combined in playful, innovative ways.

Maybe it won't end spam forever, but at least it’ll fund your coffee.

## Try it yourself (soon™)

We're still building out the open-source reference implementation, but it'll be available for curious devs and adventurous users soon.

In the meantime, thoughts? Feedback? Ready to become a paid messaging influencer?

Leave a comment—no CRC required (for now).

