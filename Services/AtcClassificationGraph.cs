using System;
using System.Collections.Generic;
using System.Linq;

namespace HospitalManagementSystem.Services
{
    /// <summary>
    /// Represents a node in the Anatomical Therapeutic Chemical (ATC) classification graph.
    /// Provides hierarchical tree traversal for drug-drug interactions and cross-reactivity.
    /// </summary>
    public class AtcNode
    {
        public string Code { get; }
        public string Name { get; }
        public AtcNode? Parent { get; private set; }
        public List<AtcNode> Children { get; } = new();

        public AtcNode(string code, string name)
        {
            Code = code;
            Name = name;
        }

        public void AddChild(AtcNode child)
        {
            child.Parent = this;
            Children.Add(child);
        }

        public bool IsDescendantOf(string codeOrName)
        {
            var curr = Parent;
            while (curr != null)
            {
                if (string.Equals(curr.Code, codeOrName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(curr.Name, codeOrName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                curr = curr.Parent;
            }
            return false;
        }

        public IEnumerable<AtcNode> GetAncestors()
        {
            var curr = Parent;
            while (curr != null)
            {
                yield return curr;
                curr = curr.Parent;
            }
        }
    }

    /// <summary>
    /// Represents a high-risk clinical interaction edge between two ATC nodes or classes.
    /// </summary>
    public record DdiRule(
        string ClassCodeA,
        string ClassCodeB,
        SafetyWarningSeverity Severity,
        string InteractionName,
        string DescriptionTemplate);

    /// <summary>
    /// Hierarchical ATC engine providing deterministic O(1) class resolution,
    /// allergy cross-reactivity tree traversal, and zero-latency Drug-Drug Interaction evaluation.
    /// </summary>
    public static class AtcClassificationGraph
    {
        private static readonly Dictionary<string, AtcNode> NodesByCode = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, AtcNode> SynonymToNode = new(StringComparer.OrdinalIgnoreCase);
        private static readonly List<DdiRule> InteractionRules = new();

        static AtcClassificationGraph()
        {
            BuildAtcTree();
            BuildInteractionRules();
        }

        private static void RegisterNode(AtcNode node, params string[] synonyms)
        {
            NodesByCode[node.Code] = node;
            SynonymToNode[node.Code] = node;
            SynonymToNode[node.Name] = node;
            foreach (var s in synonyms)
            {
                if (!string.IsNullOrWhiteSpace(s))
                {
                    SynonymToNode[s] = node;
                }
            }
        }

        private static void BuildAtcTree()
        {
            // Level 0: Root
            var root = new AtcNode("ATC", "ATC Root");
            RegisterNode(root);

            // Level 1: Broad Classes
            var antinfectives = new AtcNode("J", "Antiinfectives");
            root.AddChild(antinfectives);
            RegisterNode(antinfectives);

            var blood = new AtcNode("B", "Blood and Blood Forming Organs");
            root.AddChild(blood);
            RegisterNode(blood);

            var cardiovascular = new AtcNode("C", "Cardiovascular System");
            root.AddChild(cardiovascular);
            RegisterNode(cardiovascular);

            var musculoskeletal = new AtcNode("M", "Musculo-Skeletal System");
            root.AddChild(musculoskeletal);
            RegisterNode(musculoskeletal);

            var nervous = new AtcNode("N", "Nervous System");
            root.AddChild(nervous);
            RegisterNode(nervous);

            var genitourinary = new AtcNode("G", "Genito-Urinary System");
            root.AddChild(genitourinary);
            RegisterNode(genitourinary);

            var antineoplastic = new AtcNode("L", "Antineoplastic and Immunomodulating Agents");
            root.AddChild(antineoplastic);
            RegisterNode(antineoplastic);

            var respiratory = new AtcNode("R", "Respiratory System");
            root.AddChild(respiratory);
            RegisterNode(respiratory);

            // J: Beta-Lactams Parent Node
            var betaLactams = new AtcNode("BETA_LACTAM", "Beta-Lactams");
            antinfectives.AddChild(betaLactams);
            RegisterNode(betaLactams, "Beta-Lactam", "Beta Lactam", "Beta-Lactam Antibiotics");

            // J01C: Penicillins
            var penicillins = new AtcNode("J01C", "Penicillins");
            betaLactams.AddChild(penicillins);
            RegisterNode(penicillins, "Penicillin", "Penicillins", "Pen-VK", "Benzylpenicillin");

            var amoxicillin = new AtcNode("J01CA04", "Amoxicillin");
            penicillins.AddChild(amoxicillin);
            RegisterNode(amoxicillin, "Amoxil", "Moxacil", "Amoxi");

            var ampicillin = new AtcNode("J01CA01", "Ampicillin");
            penicillins.AddChild(ampicillin);
            RegisterNode(ampicillin);

            var augmentin = new AtcNode("J01CR02", "Amoxicillin and Beta-Lactamase Inhibitor");
            penicillins.AddChild(augmentin);
            RegisterNode(augmentin, "Augmentin", "Clavulanate", "Amoxicillin/Clavulanic Acid", "Co-amoxiclav");

            var flucloxacillin = new AtcNode("J01CF05", "Flucloxacillin");
            penicillins.AddChild(flucloxacillin);
            RegisterNode(flucloxacillin, "Cloxacillin");

            // J01D: Cephalosporins
            var cephalosporins = new AtcNode("J01D", "Cephalosporins");
            betaLactams.AddChild(cephalosporins);
            RegisterNode(cephalosporins, "Cephalosporin", "Cefalosporin");

            var cefuroxime = new AtcNode("J01DC02", "Cefuroxime");
            cephalosporins.AddChild(cefuroxime);
            RegisterNode(cefuroxime, "Kilbac", "Ceftin", "Zinacef");

            var ceftriaxone = new AtcNode("J01DD04", "Ceftriaxone");
            cephalosporins.AddChild(ceftriaxone);
            RegisterNode(ceftriaxone, "Rocephin");

            var cefalexin = new AtcNode("J01DB01", "Cefalexin");
            cephalosporins.AddChild(cefalexin);
            RegisterNode(cefalexin, "Cephalexin", "Keflex");

            var cefixime = new AtcNode("J01DD08", "Cefixime");
            cephalosporins.AddChild(cefixime);
            RegisterNode(cefixime, "Suprax");

            // J01E: Sulfonamides
            var sulfonamides = new AtcNode("J01E", "Sulfonamides");
            antinfectives.AddChild(sulfonamides);
            RegisterNode(sulfonamides, "Sulfa", "Sulpha", "Sulfonamide");

            var cotrimoxazole = new AtcNode("J01EE01", "Sulfamethoxazole and Trimethoprim");
            sulfonamides.AddChild(cotrimoxazole);
            RegisterNode(cotrimoxazole, "Bactrim", "Septra", "Cotrimoxazole", "Co-trimoxazole");

            // J01M: Quinolones / Fluoroquinolones
            var quinolones = new AtcNode("J01M", "Fluoroquinolones");
            antinfectives.AddChild(quinolones);
            RegisterNode(quinolones, "Quinolone", "Quinolones", "Fluoroquinolone");

            var ciprofloxacin = new AtcNode("J01MA02", "Ciprofloxacin");
            quinolones.AddChild(ciprofloxacin);
            RegisterNode(ciprofloxacin, "Cipro", "Ciproflox");

            var levofloxacin = new AtcNode("J01MA12", "Levofloxacin");
            quinolones.AddChild(levofloxacin);
            RegisterNode(levofloxacin, "Levaquin");

            // J01F: Macrolides
            var macrolides = new AtcNode("J01F", "Macrolides");
            antinfectives.AddChild(macrolides);
            RegisterNode(macrolides, "Macrolide");

            var azithromycin = new AtcNode("J01FA10", "Azithromycin");
            macrolides.AddChild(azithromycin);
            RegisterNode(azithromycin, "Zithromax", "Azithral", "Zmax");

            // M01A: NSAIDs
            var nsaids = new AtcNode("M01A", "NSAIDs");
            musculoskeletal.AddChild(nsaids);
            RegisterNode(nsaids, "NSAID", "Non-steroidal Anti-inflammatory Drugs");

            var ibuprofen = new AtcNode("M01AE01", "Ibuprofen");
            nsaids.AddChild(ibuprofen);
            RegisterNode(ibuprofen, "Brufen", "Advil", "Motrin");

            var naproxen = new AtcNode("M01AE02", "Naproxen");
            nsaids.AddChild(naproxen);
            RegisterNode(naproxen, "Aleve", "Naprosyn");

            var diclofenac = new AtcNode("M01AB05", "Diclofenac");
            nsaids.AddChild(diclofenac);
            RegisterNode(diclofenac, "Voltaren", "Clofenac");

            var ketorolac = new AtcNode("M01AB15", "Ketorolac");
            nsaids.AddChild(ketorolac);
            RegisterNode(ketorolac, "Toradol");

            // B01A: Anticoagulants
            var anticoagulants = new AtcNode("B01A", "Anticoagulants");
            blood.AddChild(anticoagulants);
            RegisterNode(anticoagulants, "Anticoagulant", "Blood Thinner");

            var warfarin = new AtcNode("B01AA03", "Warfarin");
            anticoagulants.AddChild(warfarin);
            RegisterNode(warfarin, "Coumadin");

            var rivaroxaban = new AtcNode("B01AF01", "Rivaroxaban");
            anticoagulants.AddChild(rivaroxaban);
            RegisterNode(rivaroxaban, "Xarelto");

            // B01AC: Antiplatelets
            var antiplatelets = new AtcNode("B01AC", "Antiplatelet Agents");
            blood.AddChild(antiplatelets);
            RegisterNode(antiplatelets, "Antiplatelet");

            var aspirin = new AtcNode("B01AC06", "Aspirin");
            antiplatelets.AddChild(aspirin);
            RegisterNode(aspirin, "Acetylsalicylic Acid", "Ecosprin", "Disprin");

            var clopidogrel = new AtcNode("B01AC04", "Clopidogrel");
            antiplatelets.AddChild(clopidogrel);
            RegisterNode(clopidogrel, "Plavix");

            // G04BE: PDE5 Inhibitors
            var pde5Inhibitors = new AtcNode("G04BE", "PDE5 Inhibitors");
            genitourinary.AddChild(pde5Inhibitors);
            RegisterNode(pde5Inhibitors, "PDE5 Inhibitor", "Phosphodiesterase-5 Inhibitor");

            var sildenafil = new AtcNode("G04BE03", "Sildenafil");
            pde5Inhibitors.AddChild(sildenafil);
            RegisterNode(sildenafil, "Viagra");

            var tadalafil = new AtcNode("G04BE08", "Tadalafil");
            pde5Inhibitors.AddChild(tadalafil);
            RegisterNode(tadalafil, "Cialis");

            // C01DA: Nitrates
            var nitrates = new AtcNode("C01DA", "Organic Nitrates");
            cardiovascular.AddChild(nitrates);
            RegisterNode(nitrates, "Nitrate", "Nitrates");

            var nitroglycerin = new AtcNode("C01DA02", "Nitroglycerin");
            nitrates.AddChild(nitroglycerin);
            RegisterNode(nitroglycerin, "Glyceryl Trinitrate", "Nitrostat", "Nitrospray");

            var isosorbide = new AtcNode("C01DA08", "Isosorbide Dinitrate");
            nitrates.AddChild(isosorbide);
            RegisterNode(isosorbide, "Isordil", "Isosorbide Mononitrate", "Imdur");

            // C09: ACE Inhibitors and ARBs
            var raasInhibitors = new AtcNode("C09", "RAAS Inhibitors");
            cardiovascular.AddChild(raasInhibitors);
            RegisterNode(raasInhibitors, "ACE Inhibitors and ARBs", "RAAS Blockers");

            var aceInhibitors = new AtcNode("C09A", "ACE Inhibitors");
            raasInhibitors.AddChild(aceInhibitors);
            RegisterNode(aceInhibitors, "ACE Inhibitor");

            var enalapril = new AtcNode("C09AA02", "Enalapril");
            aceInhibitors.AddChild(enalapril);
            RegisterNode(enalapril, "Vasotec");

            var ramipril = new AtcNode("C09AA05", "Ramipril");
            aceInhibitors.AddChild(ramipril);
            RegisterNode(ramipril, "Altace");

            var arbs = new AtcNode("C09C", "Angiotensin Receptor Blockers");
            raasInhibitors.AddChild(arbs);
            RegisterNode(arbs, "ARB", "Angiotensin II Antagonists");

            var losartan = new AtcNode("C09CA01", "Losartan");
            arbs.AddChild(losartan);
            RegisterNode(losartan, "Cozaar");

            // C03D / Potassium Sparing Agents
            var potassiumAgents = new AtcNode("C03D_A12", "Potassium-Sparing Agents and Supplements");
            cardiovascular.AddChild(potassiumAgents);
            RegisterNode(potassiumAgents, "Potassium Sparing", "Potassium Supplement");

            var spironolactone = new AtcNode("C03DA01", "Spironolactone");
            potassiumAgents.AddChild(spironolactone);
            RegisterNode(spironolactone, "Aldactone");

            var potassiumChloride = new AtcNode("A12BA01", "Potassium Chloride");
            potassiumAgents.AddChild(potassiumChloride);
            RegisterNode(potassiumChloride, "K-Lor", "Slow-K", "Potassium");

            // N06AB: SSRIs
            var ssris = new AtcNode("N06AB", "SSRIs");
            nervous.AddChild(ssris);
            RegisterNode(ssris, "SSRI", "Selective Serotonin Reuptake Inhibitors");

            var fluoxetine = new AtcNode("N06AB03", "Fluoxetine");
            ssris.AddChild(fluoxetine);
            RegisterNode(fluoxetine, "Prozac");

            var sertraline = new AtcNode("N06AB06", "Sertraline");
            ssris.AddChild(sertraline);
            RegisterNode(sertraline, "Zoloft");

            var escitalopram = new AtcNode("N06AB10", "Escitalopram");
            ssris.AddChild(escitalopram);
            RegisterNode(escitalopram, "Lexapro", "Cipralex");

            // N02A: Opioids / Serotonergic Analgesics
            var opioids = new AtcNode("N02A", "Opioids");
            nervous.AddChild(opioids);
            RegisterNode(opioids, "Opioid", "Opioids");

            var tramadol = new AtcNode("N02AX02", "Tramadol");
            opioids.AddChild(tramadol);
            RegisterNode(tramadol, "Ultram");

            var morphine = new AtcNode("N02AA01", "Morphine");
            opioids.AddChild(morphine);
            RegisterNode(morphine);

            // N06A: MAOIs
            var maois = new AtcNode("N06AF", "Monoamine Oxidase Inhibitors");
            nervous.AddChild(maois);
            RegisterNode(maois, "MAOI", "MAO Inhibitor", "Linezolid");

            // L01BA: Methotrexate
            var methotrexate = new AtcNode("L01BA01", "Methotrexate");
            antineoplastic.AddChild(methotrexate);
            RegisterNode(methotrexate, "MTX", "Trexall");

            // R03DA: Xanthines / Theophylline
            var xanthines = new AtcNode("R03DA", "Xanthines");
            respiratory.AddChild(xanthines);
            RegisterNode(xanthines, "Xanthine");

            var theophylline = new AtcNode("R03DA04", "Theophylline");
            xanthines.AddChild(theophylline);
            RegisterNode(theophylline, "Aminophylline", "Uniphyl");

            // C07: Beta Blockers
            var betaBlockers = new AtcNode("C07", "Beta Blockers");
            cardiovascular.AddChild(betaBlockers);
            RegisterNode(betaBlockers, "Beta Blocker", "Beta-Adrenergic Blockers");

            var atenolol = new AtcNode("C07AB03", "Atenolol");
            betaBlockers.AddChild(atenolol);
            RegisterNode(atenolol, "Tenormin");

            var metoprolol = new AtcNode("C07AB02", "Metoprolol");
            betaBlockers.AddChild(metoprolol);
            RegisterNode(metoprolol, "Betaloc", "Lopressor");

            // C08D: Non-dihydropyridine Calcium Channel Blockers
            var ccbNonDihydropyridines = new AtcNode("C08D", "Non-Dihydropyridine CCBs");
            cardiovascular.AddChild(ccbNonDihydropyridines);
            RegisterNode(ccbNonDihydropyridines, "Verapamil and Diltiazem");

            var verapamil = new AtcNode("C08DA01", "Verapamil");
            ccbNonDihydropyridines.AddChild(verapamil);
            RegisterNode(verapamil, "Calan", "Isoptin");

            var diltiazem = new AtcNode("C08DB01", "Diltiazem");
            ccbNonDihydropyridines.AddChild(diltiazem);
            RegisterNode(diltiazem, "Cardizem");
        }

        private static void BuildInteractionRules()
        {
            // 1. Anticoagulants (B01A) + NSAIDs (M01A) -> Critical Severe Bleeding Risk
            InteractionRules.Add(new DdiRule(
                "B01A", "M01A",
                SafetyWarningSeverity.Critical,
                "Severe Hemorrhagic Risk",
                "Severe Bleeding Risk: Concomitant administration of anticoagulant ({0}) and NSAID ({1}) drastically impairs platelet aggregation and gastrointestinal mucosal integrity."));

            // 2. PDE5 Inhibitors (G04BE) + Nitrates (C01DA) -> Critical Hypotension
            InteractionRules.Add(new DdiRule(
                "G04BE", "C01DA",
                SafetyWarningSeverity.Critical,
                "Severe Hypotension / Syncope",
                "Precipitous Hypotension Risk: Co-administration of PDE5 inhibitor ({0}) with organic nitrate ({1}) potentiates cGMP-mediated vasodilation, risking fatal hypotension and cardiovascular collapse."));

            // 3. RAAS Inhibitors (C09) + Potassium-Sparing Diuretics/Potassium (C03D_A12) -> Critical Hyperkalemia
            InteractionRules.Add(new DdiRule(
                "C09", "C03D_A12",
                SafetyWarningSeverity.Critical,
                "Severe Hyperkalemia Risk",
                "Severe Hyperkalemia: Combining renin-angiotensin-aldosterone inhibitor ({0}) with potassium-sparing agent or supplement ({1}) risks life-threatening arrhythmias."));

            // 4. SSRIs (N06AB) + Tramadol / MAOIs (N02AX02 / N06AF) -> Critical Serotonin Syndrome
            InteractionRules.Add(new DdiRule(
                "N06AB", "N02AX02",
                SafetyWarningSeverity.Critical,
                "Serotonin Syndrome Risk",
                "Serotonin Toxicity: Concomitant SSRI ({0}) and Tramadol ({1}) elevates synaptic serotonin, risking Serotonin Syndrome (hyperthermia, clonus, autonomic instability)."));

            InteractionRules.Add(new DdiRule(
                "N06AB", "N06AF",
                SafetyWarningSeverity.Critical,
                "Serotonin Syndrome Risk",
                "Potentially Fatal Serotonin Syndrome: Combining SSRI ({0}) with MAO inhibitor ({1}) produces severe central serotonin toxicity."));

            // 5. Methotrexate (L01BA01) + NSAIDs (M01A) -> Critical Methotrexate Toxicity
            InteractionRules.Add(new DdiRule(
                "L01BA01", "M01A",
                SafetyWarningSeverity.Critical,
                "Methotrexate Toxicity",
                "Severe Bone Marrow & Renal Toxicity: NSAID ({1}) reduces renal clearance and protein binding of methotrexate ({0}), risking fatal toxicity."));

            // 6. Fluoroquinolones (J01M) + Theophylline (R03DA04) -> Warning Theophylline Toxicity
            InteractionRules.Add(new DdiRule(
                "J01M", "R03DA04",
                SafetyWarningSeverity.Warning,
                "Theophylline Clearance Inhibition",
                "CYP1A2 Inhibition: Fluoroquinolone ({0}) inhibits theophylline ({1}) metabolism, potentially elevating serum theophylline to toxic, seizure-inducing levels."));

            // 7. Beta Blockers (C07) + Non-dihydropyridine CCBs (C08D) -> Warning Severe Bradycardia / AV Block
            InteractionRules.Add(new DdiRule(
                "C07", "C08D",
                SafetyWarningSeverity.Warning,
                "Severe Bradycardia / Heart Block",
                "Cardiodepressant Synergism: Concomitant beta-blocker ({0}) and non-dihydropyridine CCB ({1}) risks severe bradycardia, AV nodal conduction block, and cardiogenic decompensation."));
        }

        /// <summary>
        /// Finds the ATC node for a given medicine generic or name.
        /// Resolves exact matches or tokenized matches in O(1).
        /// </summary>
        public static AtcNode? FindNode(string? nameOrGeneric)
        {
            if (string.IsNullOrWhiteSpace(nameOrGeneric)) return null;

            var cleaned = nameOrGeneric.Trim();
            if (SynonymToNode.TryGetValue(cleaned, out var directNode))
            {
                return directNode;
            }

            // Clean common suffix/dosage strings (e.g., "Amoxicillin 500mg" -> "Amoxicillin")
            var tokens = cleaned.Split(new[] { ' ', '-', '/', ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                if (SynonymToNode.TryGetValue(token, out var tokenNode))
                {
                    return tokenNode;
                }
            }

            return null;
        }

        /// <summary>
        /// Determines if a medicine belongs to or is descended from a target class/node.
        /// </summary>
        public static bool IsInClass(AtcNode? node, string targetCodeOrName)
        {
            if (node == null) return false;
            if (string.Equals(node.Code, targetCodeOrName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(node.Name, targetCodeOrName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return node.IsDescendantOf(targetCodeOrName);
        }

        /// <summary>
        /// Evaluates all clinical drug-drug interaction rules against a pair of medicines.
        /// Returns any matched safety warnings with deterministic clinical messaging.
        /// </summary>
        public static IEnumerable<SafetyWarning> EvaluateInteraction(
            string medAName, string medAGeneric,
            string medBName, string medBGeneric)
        {
            var nodeA = FindNode(medAGeneric) ?? FindNode(medAName);
            var nodeB = FindNode(medBGeneric) ?? FindNode(medBName);

            if (nodeA == null || nodeB == null)
            {
                yield break;
            }

            foreach (var rule in InteractionRules)
            {
                bool matchDirect = IsInClass(nodeA, rule.ClassCodeA) && IsInClass(nodeB, rule.ClassCodeB);
                bool matchReverse = IsInClass(nodeB, rule.ClassCodeA) && IsInClass(nodeA, rule.ClassCodeB);

                if (matchDirect)
                {
                    var msg = string.Format(rule.DescriptionTemplate, $"{medAName} ({medAGeneric})", $"{medBName} ({medBGeneric})");
                    yield return new SafetyWarning("Drug-Drug Interaction", rule.Severity, msg);
                }
                else if (matchReverse)
                {
                    var msg = string.Format(rule.DescriptionTemplate, $"{medBName} ({medBGeneric})", $"{medAName} ({medAGeneric})");
                    yield return new SafetyWarning("Drug-Drug Interaction", rule.Severity, msg);
                }
            }
        }

        /// <summary>
        /// Verifies whether a given allergy substance matches a medicine via ATC graph cross-reactivity.
        /// </summary>
        public static bool MatchesAllergyClass(string substanceOrAllergen, string medName, string medGeneric)
        {
            var allergyNode = FindNode(substanceOrAllergen);
            var medNode = FindNode(medGeneric) ?? FindNode(medName);

            if (allergyNode != null && medNode != null)
            {
                if (medNode == allergyNode ||
                    medNode.IsDescendantOf(allergyNode.Code) ||
                    medNode.IsDescendantOf(allergyNode.Name))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
