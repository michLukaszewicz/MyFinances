import axios from 'axios';
import type { Transaction } from '../Models/Transaction';

const transactionService = {
  async getTestData(): Promise<Transaction[]> {
    const response = await axios.get<Transaction[]>(`/testdata`);

    return response.data.map(t => ({
      ...t,
      date: new Date(t.date), 
    }));
  }
};

export default transactionService;
