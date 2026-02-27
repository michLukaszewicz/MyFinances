import axios from "axios";
import type { Transaction } from "../../Models/Transaction";
import { API_ROUTES } from "../../Routes/RoutesConsts";

const TransactionService = {
  async getTestData(): Promise<Transaction[]> {
    const response = await axios.get<Transaction[]>(`${API_ROUTES.base}/TestData`);
    return response.data.map((t) => ({
      ...t,
      date: new Date(t.date),
    }));
  },

  async postTransaction(newTransaction: Transaction): Promise<number> {
    const response = await axios.post<number>(`${API_ROUTES.base}/TestData`, {
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

  async deleteTransaction(id: number): Promise<void> {
    await axios.delete<void>(`${API_ROUTES.base}/TestData/${id}`);
  },

  async getTransactionHistory(): Promise<Transaction[]> {
    const token = localStorage.getItem("access_token");
    if (!token) {
      throw new Error("No access token found. Please log in.");
    }
    const response = await axios.get<Transaction[]>(`${API_ROUTES.base}/Transactions`, {
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
