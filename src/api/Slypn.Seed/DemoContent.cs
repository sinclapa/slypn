namespace Slypn.Seed;

public sealed record DemoArticle(
    string Title, string Summary, IReadOnlyList<string> Paragraphs,
    IReadOnlyList<string> Tags, string Category, string Author);

public sealed record DemoResource(string Title, string Description, string Url, string Category);

/// <summary>One-off demo event at a month offset from today (negative = past).</summary>
public sealed record DemoEvent(
    int MonthOffset, int Day, int StartHour, int StartMinute, int EndHour, int EndMinute,
    string Title, string Type, string Location, string Description, string? SignupUrl);

/// <summary>
/// Demo content pool for local seeding — plausible working-age Parkinson's
/// material in the spirit of slypn.org.uk and parkinsons.org.uk. Not medical
/// advice; placeholder copy for development only.
/// </summary>
public static class DemoContent
{
    public static readonly IReadOnlyList<DemoArticle> Articles = new[]
    {
        new DemoArticle(
            "Telling your employer about Parkinson's",
            "When to disclose a diagnosis at work, what your rights are, and how to frame the conversation with a manager.",
            new[]
            {
                "There is no single right time to tell an employer you have Parkinson's. Some members tell us they disclosed early and were glad of the support; others waited until symptoms started to affect specific tasks.",
                "In the UK, Parkinson's is covered by the Equality Act 2010. That means your employer must make reasonable adjustments once they know — flexible hours, a quieter desk, or more time for tasks affected by your best-medication window.",
                "Go into the conversation with a short list of the adjustments that would actually help. It turns an awkward disclosure into a practical, forward-looking discussion.",
            },
            new[] { "work", "rights", "disclosure" }, "Living with Parkinson's", "Helen Stoinanov"),

        new DemoArticle(
            "Understanding levodopa and your medication window",
            "A plain-English look at how levodopa works, why timing matters, and what an \"off\" period actually feels like.",
            new[]
            {
                "Levodopa is still the most effective treatment for the motor symptoms of Parkinson's. The body converts it into dopamine, topping up what the brain no longer makes enough of.",
                "Many people notice their medication working in a window — a couple of good hours after a dose, then a dip as it wears off. Keeping a simple diary of doses and how you feel helps your specialist nurse fine-tune the timing.",
                "If you are getting \"off\" periods before your next dose is due, that is worth raising at your next appointment rather than quietly tolerating it.",
            },
            new[] { "medication", "levodopa", "symptoms" }, "Treatment", "Kate Wellington"),

        new DemoArticle(
            "Exercise and Parkinson's: what the evidence says",
            "Cutting through the noise on lifestyle interventions — what actually helps slow symptom progression, and where to start.",
            new[]
            {
                "Of all the lifestyle advice for Parkinson's, exercise has the strongest evidence behind it. Aerobic and resistance training are both associated with slower symptom progression and better day-to-day function.",
                "You do not need a gym. Brisk walking, cycling, dancing and boxing-style classes all count, and many members find a class easier to stick with than exercising alone.",
                "Start small and build a habit. Twenty minutes most days beats an ambitious plan you abandon after a fortnight.",
            },
            new[] { "exercise", "evidence", "wellbeing" }, "Lifestyle", "Daniel Okafor"),

        new DemoArticle(
            "Sleep, fatigue and the non-motor side of Parkinson's",
            "The symptoms no one warned you about — disrupted sleep, daytime fatigue, and practical things that help.",
            new[]
            {
                "Parkinson's is often described in terms of tremor and stiffness, but for many people the non-motor symptoms — poor sleep, fatigue, low mood — have the bigger impact on daily life.",
                "Good sleep hygiene helps: a consistent bedtime, less screen time late on, and reviewing whether any medications are affecting your rest.",
                "Fatigue is real and not a sign of laziness. Pacing your day around your best hours, and being honest with the people around you, makes a genuine difference.",
            },
            new[] { "sleep", "fatigue", "non-motor" }, "Living with Parkinson's", "Sarah Webb"),

        new DemoArticle(
            "Deep brain stimulation: who it might suit",
            "What DBS is, the kind of symptoms it helps, and how the referral and assessment process works.",
            new[]
            {
                "Deep brain stimulation (DBS) uses a small implanted device to send signals to specific areas of the brain. It can help with tremor and with the fluctuations that come from years of medication.",
                "DBS is not for everyone, and it is not a cure. A specialist team assesses whether the likely benefits outweigh the risks for you specifically.",
                "If you are curious, the first step is a conversation with your neurologist about whether a referral for assessment makes sense.",
            },
            new[] { "DBS", "treatment", "surgery" }, "Treatment", "Priya Iyer"),

        new DemoArticle(
            "Driving and Parkinson's: what you need to know",
            "Your legal duties, telling the DVLA, and keeping your independence on the road for as long as it is safe.",
            new[]
            {
                "In the UK you must tell the DVLA if you are diagnosed with Parkinson's. It does not automatically mean losing your licence — many people continue to drive safely for years.",
                "You will usually keep a licence that is reviewed periodically, and you should also inform your insurer.",
                "If the time does come to stop, planning ahead for alternatives — local transport, lifts from the network — makes the change feel less abrupt.",
            },
            new[] { "driving", "DVLA", "independence" }, "Living with Parkinson's", "Helen Stoinanov"),

        new DemoArticle(
            "Eating well when medication and food compete",
            "Why protein timing can affect levodopa, and simple ways to keep meals enjoyable without losing the benefit of your dose.",
            new[]
            {
                "Levodopa and dietary protein use the same transport system to get into the body, so a large protein-heavy meal can blunt how well a dose works for some people.",
                "A common approach is to take levodopa around 30–45 minutes before eating, and to spread protein across the day rather than in one big hit.",
                "Everyone is different — a dietitian or your specialist nurse can help you find a pattern that protects your medication without making food a chore.",
            },
            new[] { "diet", "levodopa", "nutrition" }, "Treatment", "Kate Wellington"),

        new DemoArticle(
            "Building a support network at any age",
            "Why peer support matters more than people expect, and how a working-age network is different from general groups.",
            new[]
            {
                "We started this network because the support that existed was rarely built around people still working, raising children, or newly diagnosed in their forties.",
                "Peer support is not about swapping symptoms. It is the relief of being in a room where you do not have to explain yourself.",
                "If you are new, you do not need to bring anything but yourself. Come for a coffee and see how it feels.",
            },
            new[] { "peer-support", "community", "newcomers" }, "Community", "Sarah Webb"),

        new DemoArticle(
            "Talking to your children about your diagnosis",
            "Age-appropriate ways to explain Parkinson's to children, and reassurance for the conversations that worry parents most.",
            new[]
            {
                "Children usually notice more than we think. A simple, honest explanation tends to land better than silence, which can leave them imagining something worse.",
                "Keep it concrete and age-appropriate: a part of the brain makes less of a chemical that helps movement, the medicine helps, and you are still you.",
                "Let them ask questions over time. One conversation rarely covers everything, and that is fine.",
            },
            new[] { "family", "children", "wellbeing" }, "Living with Parkinson's", "Daniel Okafor"),

        new DemoArticle(
            "Mindfulness, mood and managing anxiety",
            "Low mood and anxiety are common with Parkinson's. What helps, what to watch for, and when to ask for more support.",
            new[]
            {
                "Anxiety and low mood are part of Parkinson's for many people, partly because of the same brain changes that affect movement — not a personal failing.",
                "Simple practices help some people: breathing exercises, gentle routine, time outdoors, and staying socially connected even when you do not feel like it.",
                "If low mood persists, talk to your GP or specialist nurse. Effective support exists, and asking for it early is a strength.",
            },
            new[] { "mental-health", "anxiety", "wellbeing" }, "Lifestyle", "Priya Iyer"),
    };

    public static readonly IReadOnlyList<DemoArticle> Blogs = new[]
    {
        new DemoArticle(
            "Brixton coffee meet-up — a warm recap",
            "Eleven of us, two new faces, and far too much cake. A short write-up from Tuesday evening.",
            new[]
            {
                "We had a brilliant turnout at our Brixton meet-up — eleven members, including two people newly diagnosed who came along for the first time.",
                "As ever, the conversation ranged from medication timing to the football. That mix is exactly the point.",
                "Next month we are back on the South Bank. Bring a friend.",
            },
            new[] { "meet-up", "recap" }, "Community", "Sarah Webb"),

        new DemoArticle(
            "Welcome to our newest members",
            "A short hello to the people who have joined the network this season.",
            new[]
            {
                "It has been a busy few months and we are delighted to welcome several new members to the network.",
                "If you have just joined, your first meet-up can feel like a big step. It is not — people will be glad to see you.",
                "Come and say hello at the next coffee morning.",
            },
            new[] { "welcome", "members" }, "Community", "Helen Stoinanov"),

        new DemoArticle(
            "Thank you to our spring 10k runners",
            "Our spring fundraiser raised money for Parkinson's research. Thank you to everyone who ran, walked or cheered.",
            new[]
            {
                "On a damp Sunday morning a group of us turned out to run, jog and walk the spring 10k.",
                "Between sponsorship and matched donations we raised a fantastic total for Parkinson's research.",
                "Thank you to everyone who took part, and to the friends and family who stood in the rain to cheer.",
            },
            new[] { "fundraising", "events" }, "Fundraising", "Kate Wellington"),

        new DemoArticle(
            "Q&A with a movement-disorder nurse",
            "Highlights from our online session on medication timing, sleep and getting the most from appointments.",
            new[]
            {
                "We were lucky to host a specialist movement-disorder nurse for an hour of questions from members.",
                "The biggest theme was medication timing — and how a simple diary makes appointments far more productive.",
                "We will share a fuller write-up in the next newsletter.",
            },
            new[] { "q-and-a", "treatment" }, "News", "Priya Iyer"),

        new DemoArticle(
            "Notes from the carers' catch-up",
            "A small, honest session for partners and carers. A few reflections from the evening.",
            new[]
            {
                "Our carers' catch-up gives partners and carers a space of their own, away from appointments and admin.",
                "This month the conversation turned to looking after your own wellbeing — and why that is not selfish.",
                "If you care for someone in the network, you are very welcome to come along.",
            },
            new[] { "carers", "support" }, "Community", "Daniel Okafor"),

        new DemoArticle(
            "Summer social by the river",
            "Drinks, sunshine and good company at our summer get-together.",
            new[]
            {
                "Our summer social brought members, partners and carers together for a relaxed evening by the river.",
                "No agenda, no talks — just good company and a chance to catch up properly.",
                "Photos to follow in the next newsletter.",
            },
            new[] { "social", "events" }, "Events", "Sarah Webb"),

        new DemoArticle(
            "Five things we learned this year",
            "A short retrospective from the organisers as another year of meet-ups wraps up.",
            new[]
            {
                "Another year of coffee mornings, talks and the occasional 10k. A few things stood out.",
                "People value consistency — the same time, the same place — more than big one-off events.",
                "And new members almost always say the same thing afterwards: I wish I'd come sooner.",
            },
            new[] { "retrospective", "community" }, "News", "Helen Stoinanov"),

        new DemoArticle(
            "A member's story: the first year",
            "One member shares what helped most in the twelve months after diagnosis.",
            new[]
            {
                "When I was diagnosed I went straight to the internet, which was a mistake. What actually helped was talking to people a few years ahead of me.",
                "Exercise gave me something to control. The network gave me people who got it without explanation.",
                "If you are at the start of this, be kind to yourself. It gets less overwhelming.",
            },
            new[] { "member-story", "newly-diagnosed" }, "Community", "Kate Wellington"),

        new DemoArticle(
            "Volunteers wanted for the autumn programme",
            "We are planning the autumn meet-ups and could use a few extra hands.",
            new[]
            {
                "Our meet-ups run because members give a little time to help organise them.",
                "We are looking for a few volunteers to help with the autumn programme — nothing onerous, just a few hours here and there.",
                "If you can help, have a word with one of the organisers at the next meet-up.",
            },
            new[] { "volunteering", "events" }, "News", "Daniel Okafor"),

        new DemoArticle(
            "Why we meet on the South Bank",
            "A note on our regular venue and what makes it work for the group.",
            new[]
            {
                "The Royal Festival Hall has become our home for monthly coffee mornings, and there are good reasons it works.",
                "It is step-free, easy to reach by train and bus, and there is always somewhere to sit and talk.",
                "If you have never been, the last Saturday of the month is the one to put in your diary.",
            },
            new[] { "venue", "meet-up" }, "Community", "Priya Iyer"),
    };

    public static readonly IReadOnlyList<DemoResource> Resources = new[]
    {
        new DemoResource(
            "Parkinson's UK helpline",
            "Free, confidential support from trained advisers and specialist nurses. Mon–Fri 9am–6pm, Sat 10am–2pm.",
            "https://www.parkinsons.org.uk/information-and-support/helpline-and-local-advisers",
            "Parkinson's UK"),
        new DemoResource(
            "Newly diagnosed: where to start",
            "Parkinson's UK's starting point if you have been diagnosed in the last few months.",
            "https://www.parkinsons.org.uk/information-and-support/newly-diagnosed",
            "Parkinson's UK"),
        new DemoResource(
            "NHS — Parkinson's disease overview",
            "Plain-English summary of symptoms, treatment and what to expect from NHS care.",
            "https://www.nhs.uk/conditions/parkinsons-disease/",
            "NHS"),
        new DemoResource(
            "Personal Independence Payment (PIP)",
            "The main UK benefit you may be entitled to as a working-age adult with Parkinson's.",
            "https://www.gov.uk/pip",
            "Benefits"),
        new DemoResource(
            "Carers UK",
            "Advice, peer support and a helpline specifically for unpaid carers.",
            "https://www.carersuk.org/",
            "Carers"),
    };

    // Varied one-off events spread across the past and future, in the spirit of
    // the activities written up in SLYPN newsletters (drinks, Q&As, fundraisers,
    // carer sessions, walks and socials).
    public static readonly IReadOnlyList<DemoEvent> ExtraEvents = new[]
    {
        new DemoEvent(-8, 12, 19, 0, 22, 0, "Summer drinks — Greenwich", "Drinks",
            "The Trafalgar Tavern, Greenwich SE10",
            "Our summer social by the river. Partners and carers welcome — no booking needed.", null),
        new DemoEvent(-6, 18, 19, 30, 21, 30, "Quiz night fundraiser", "Fundraising",
            "The Effra Hall Tavern, Brixton SW2",
            "Teams of up to six. All proceeds go to Parkinson's UK research.", null),
        new DemoEvent(-4, 7, 19, 0, 20, 30, "Q&A with a movement-disorder neurologist", "Q&A",
            "Online (Zoom)",
            "An hour with a King's College Hospital specialist, with plenty of time for your questions.",
            "https://example.com/slypn/qa-neurologist"),
        new DemoEvent(-3, 24, 19, 30, 21, 0, "Carer-only catch-up", "Carer session",
            "Online (Zoom)",
            "A small, relaxed group for partners and carers of members.", null),
        new DemoEvent(-2, 15, 10, 30, 12, 30, "Autumn walk — Dulwich Park", "Activity",
            "Dulwich Park, SE21",
            "A gentle, accessible loop followed by coffee. All paces welcome.", null),
        new DemoEvent(-1, 20, 18, 30, 22, 0, "Curry night social", "Social",
            "Ganapati, Peckham SE15",
            "An informal supper. Limited places — please let us know if you're coming.", null),
        new DemoEvent(1, 9, 19, 0, 20, 30, "Q&A: medication, sleep and fatigue", "Q&A",
            "Online (Zoom)",
            "Practical strategies for the non-motor side of Parkinson's, plus your questions.",
            "https://example.com/slypn/qa-sleep"),
        new DemoEvent(2, 13, 18, 30, 22, 0, "Festive meal", "Social",
            "The Camberwell Arms, SE5",
            "Our end-of-year get-together. Limited capacity — please book.", null),
        new DemoEvent(3, 6, 14, 0, 16, 0, "Ten-pin bowling afternoon", "Activity",
            "Palace Superbowl, Elephant & Castle SE1",
            "A fun, low-key afternoon. Family welcome.", null),
        new DemoEvent(4, 21, 9, 30, 12, 0, "Beat Parkinson's 10k", "Fundraising",
            "Brockwell Park, Herne Hill SE24",
            "Run, jog or walk to raise funds for Parkinson's UK.", "https://example.com/slypn/10k"),
        new DemoEvent(5, 27, 19, 30, 21, 0, "Carer catch-up", "Carer session",
            "Online (Zoom)",
            "Space for carers to share and support one another.", null),
    };
}
