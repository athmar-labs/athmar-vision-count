import io
import unittest

from evaluate import evaluate


class AcceptanceTests(unittest.TestCase):
    def setUp(self):
        self.policy = {
            "skus": ["A", "B"], "min_sessions": 2,
            "min_positive_sessions_per_sku": 1, "min_absent_sessions_per_sku": 1,
            "min_session_exact_rate": 0.5, "max_sku_weighted_absolute_error": 0.25,
            "max_sku_false_positive_rate": 0,
        }

    def run_rows(self, rows):
        return evaluate(io.StringIO("session_id,sku,expected_count,proposed_count\n" + rows), self.policy)

    def test_reports_under_and_over_counts_without_cancelling(self):
        report = self.run_rows("s1,A,5,4\ns1,B,0,0\ns2,A,0,0\ns2,B,5,6\n")
        self.assertEqual(report["skus"]["A"]["undercount_units"], 1)
        self.assertEqual(report["skus"]["B"]["overcount_units"], 1)
        self.assertEqual(report["session_exact_rate"], 0)
        self.assertFalse(report["passed"])

    def test_exact_counts_pass_with_coverage(self):
        self.assertTrue(self.run_rows("s1,A,5,5\ns1,B,0,0\ns2,A,0,0\ns2,B,5,5\n")["passed"])

    def test_false_positive_and_missing_coverage_fail(self):
        report = self.run_rows("s1,A,5,5\ns1,B,0,1\ns2,A,5,5\ns2,B,0,0\n")
        self.assertFalse(report["passed"])
        self.assertIsNone(report["skus"]["B"]["weighted_absolute_error"])
        self.assertEqual(report["skus"]["B"]["false_positive_rate"], 0.5)

    def test_rejects_incomplete_duplicate_and_out_of_scope_rows(self):
        for rows in ("s1,A,5,5\n", "s1,A,5,5\ns1,A,5,5\n", "s1,C,0,0\n", "s1,A,-1,0\n"):
            with self.subTest(rows=rows), self.assertRaises(ValueError):
                self.run_rows(rows)


if __name__ == "__main__":
    unittest.main()
