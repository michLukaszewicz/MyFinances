import axios from 'axios';
import type { Transaction } from '../../Models/Transaction';

const transactionService = {
  async getTestData(): Promise<Transaction[]> {
    const response = await axios.get<Transaction[]>(`https://localhost:7121/TestData`);
    return response.data.map(t => ({
      ...t,
      date: new Date(t.date), 
    }));
  },

  async getTransactionHistory(): Promise<Transaction[]> {
    const token = localStorage.getItem('access_token');
    const response = await axios.get<Transaction[]>(`https://localhost:7121/Transactions`, {
      headers: {
        Authorization: `Bearer ${token}`,
      }
    });

    return response.data.map(t => ({
      ...t,
      date: new Date(t.date), 
    }));
  }
};

export default transactionService;
