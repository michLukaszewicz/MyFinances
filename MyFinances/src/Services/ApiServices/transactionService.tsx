import axios from "axios";
import type { Transaction } from "../../Models/Transaction";

const TransactionService = {
  async getTestData(): Promise<Transaction[]> {
    const response = await axios.get<Transaction[]>(`https://localhost:7121/TestData`);
    return response.data.map((t) => ({
      ...t,
      date: new Date(t.date),
    }));
  },

  async postTransactionHistory(newTransaction: Transaction): Promise<number> {
    console.log(newTransaction.bankAccount);
    const response = await axios.post<number>(`https://localhost:7121/TestData`, {
      id: 1,
      date: new Date(newTransaction.date).toISOString(),
      description: newTransaction.description,
      amount: newTransaction.amount,
      category: newTransaction.category,
      bankAccount: newTransaction.bankAccount.toString(),
      otherSideOfTransaction: newTransaction.otherSideOfTransaction,
    });
    return response.data;
  },

  async getTransactionHistory(): Promise<Transaction[]> {
    const token = localStorage.getItem("access_token");
    if (!token) {
      throw new Error("No access token found. Please log in.");
    }
    const response = await axios.get<Transaction[]>(`https://localhost:7121/Transactions`, {
      headers: {
        Authorization: `Bearer ${token}`,
      },
    });

    return response.data.map((t) => ({
      ...t,
      date: new Date(t.date),
    }));
  },
};

export default TransactionService;
